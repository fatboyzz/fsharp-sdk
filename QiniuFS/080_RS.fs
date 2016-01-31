module QiniuFS.RS

open System
open System.IO
open System.Net
open System.Text
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Util
open Client

type StatSucc = {
    hash : String
    fsize : Int64
    putTime : Int64
    mimeType : String
    endUser : String
}

type FetchSucc = {
    hash : String
    key : String
}

type CallRet = | CallSucc | CallError of Error
type StatRet = | StatSucc of StatSucc | StatError of Error
type FetchRet = | FetchSucc of FetchSucc | FetchError of Error

type Op = 
    | OpStat of Entry
    | OpDelete of Entry
    | OpCopy of src : Entry * dst : Entry
    | OpMove of src : Entry * dst : Entry

type OpRet = | OpSucc | OpStatSucc of StatSucc | OpError of Error

type OpItemRet = {
    code : HttpStatusCode
    data : String
}

let private opToUri (op : Op) =
    match op with
    | OpStat e -> String.Format("/stat/{0}", e.Encoded)
    | OpDelete e -> String.Format("/delete/{0}", e.Encoded)
    | OpCopy (src, dst) -> String.Format("/copy/{0}/{1}", src.Encoded, dst.Encoded)
    | OpMove (src, dst) -> String.Format("/move/{0}/{1}", src.Encoded, dst.Encoded)
    
let private encodeOps (ops : Op seq) =
    ops
    |> Seq.toArray
    |> Array.map (opToUri >> (fun s -> "op=" + s))
    |> String.concat "&"
    |> stringToUtf8

let internal authorization (c : Client) (req : HttpWebRequest, body : byte[]) =
    String.Format("QBox {0}", c.mac.SignRequest(req, body))

let internal requestOp (c : Client) (url : String) =
    let req = request url
    req.Method <- "GET"
    req.ContentType <- "application/x-www-form-urlencoded"
    req.Headers.Add("Authorization", authorization c (req, null))
    req

let private requestBatch (c : Client) (body : byte[]) =
    let url = String.Format("{0}/batch", c.config.rsHost)
    let req = request url
    req.Method <- "POST"
    req.ContentType <- "application/x-www-form-urlencoded"
    req.Headers.Add("Authorization", authorization c (req, body))
    req

let private parseCallRet = parse (fun _ -> CallSucc) CallError
let private parseStatRet = parse StatSucc StatError
let private parseFetchRet = parse FetchSucc FetchError

let private parseOpRet (op : Op, item : OpItemRet) =
    match op, item.code |> accepted with
    | _, false -> item.data |> jsonToObject<Error> |> OpError
    | OpStat _, _ -> item.data |> jsonToObject<StatSucc> |> OpStatSucc
    | _ -> OpSucc

let private rsHostGet (c : Client) (op : Op) =
    String.Concat(c.config.rsHost, op |> opToUri)
    |> requestOp c
    |> responseJson

let checkCallRet (ret : CallRet) =
    match ret with
    | CallSucc -> ()
    | CallError e -> failwith e.error

let checkStatRet (ret : StatRet) =
    match ret with
    | StatSucc _ -> ()
    | StatError e -> failwith e.error

let checkFetchRet (ret : FetchRet) =
    match ret with
    | FetchSucc _ -> ()
    | FetchError e -> failwith e.error

let checkOpRet (ret : OpRet) =
    match ret with
    | OpSucc | OpStatSucc _ -> ()
    | OpError e -> failwith e.error

let stat (c : Client) (en : Entry) =
    rsHostGet c (OpStat en) |>> parseStatRet

let delete (c : Client) (en : Entry) = 
    rsHostGet c (OpDelete en) |>> parseCallRet

let copy (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet c (OpCopy (src, dst)) |>> parseCallRet

let move (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet c (OpMove (src, dst)) |>> parseCallRet

let fetch (c : Client) (url : String) (dst : Entry) =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.ioHost, "fetch", 
        stringToBase64Safe url, "to", dst.Encoded) 
    |> requestOp c |> responseJson |>> parseFetchRet

let changeMime (c : Client) (mime : String) (en : Entry)  =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.rsHost, "chgm",
        en.Encoded, "mime", stringToBase64Safe mime) 
    |> requestOp c |> responseJson |>> parseCallRet

let batch (c : Client) (ops : Op[]) =
    let parse (ret : bool * String) =
        match ret with
        | true, json -> 
            json 
            |> jsonToObject<OpItemRet[]>
            |> Array.zip ops
            |> Array.map parseOpRet
        | _, _ -> Array.empty
    async {
        let body = encodeOps ops
        let req = requestBatch c body
        use! output = requestStream req
        do! output.AsyncWrite(body, 0, body.Length)
        return! req |> responseJson |>> parse
    }
    