module Qiniu.RS

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
    | Stat of Entry
    | Delete of Entry
    | Copy of src : Entry * dst : Entry
    | Move of src : Entry * dst : Entry

type OpRet = | OpSucc | OpStatSucc of StatSucc | OpError of Error


type ListItem = {
    key : String
    hash : String
    fsize : Int64
    putTime : Int64
    mimeType : String
    endUser : String
}

type ListSucc = {
    marker : String
    commonPrefixes : String[]
    items : ListItem[]
}

type ListRet = | ListSucc of ListSucc | ListError of Error


let private opToUri (op : Op) =
    match op with
    | Stat e -> String.Format("/stat/{0}", e.Encoded)
    | Delete e -> String.Format("/delete/{0}", e.Encoded)
    | Copy (src, dst) -> String.Format("/copy/{0}/{1}", src.Encoded, dst.Encoded)
    | Move (src, dst) -> String.Format("/move/{0}/{1}", src.Encoded, dst.Encoded)
    
let private encodeOps (ops : Op seq) =
    ops
    |> Seq.toArray
    |> Array.map (opToUri >> (fun s -> "op=" + s))
    |> String.concat "&"
    |> stringToUtf8

let internal authorization (c : Client) (req : HttpWebRequest, body : byte[]) =
    String.Format("QBox {0}", c.mac.SignRequest(req, body))

let internal requestOp (c : Client) (url : String) =
    let req = request c url
    req.Method <- "GET"
    req.ContentType <- "application/x-www-form-urlencoded"
    req.Headers.Add("Authorization", authorization c (req, null))
    req

let private requestBatch (c : Client) (ops : Op seq) =
    let url = String.Format("{0}/batch", c.config.rsHost)
    let req = request c url
    let body = encodeOps ops
    req.Method <- "POST"
    req.ContentType <- "application/x-www-form-urlencoded"
    req.Headers.Add("Authorization", authorization c (req, body))
    req.GetRequestStream().Write(body, 0, body.Length)
    req

let private parseCallRet = parse (constf CallSucc) CallError
let private parseStatRet = parse StatSucc StatError
let private parseFetchRet = parse FetchSucc FetchError

let private parseOpItemRet (op : Op, (ok : bool, json : String)) =
    match op, ok with
    | _, false -> op, (json |> jsonToObject<Error> |> OpError)
    | Stat _, _ -> op, (json |> jsonToObject<StatSucc> |> OpStatSucc)
    | _ -> op, OpSucc

let private parseListRet = parse ListSucc ListError

let private rsHostGet (c : Client) (op : Op) =
    String.Concat(c.config.rsHost, op |> opToUri)
    |> requestOp c |> response

let stat (c : Client) (s : Entry) =
    rsHostGet c (Stat s) |> parseStatRet

let delete (c : Client) (s : Entry) = 
    rsHostGet c (Delete s) |> parseCallRet

let copy (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet c (Copy (src, dst)) |> parseCallRet

let move (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet c (Move (src, dst)) |> parseCallRet

let fetch (c : Client) (url : String) (dst : Entry) = 
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.ioHost, "fetch", 
        stringToBase64Safe url, "to", dst.Encoded) 
    |> requestOp c |> response |> parseFetchRet

let changeMime (c : Client) (mime : String) (s : Entry)  =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.rsHost, "chgm",
        s.Encoded, "mime", stringToBase64Safe mime) 
    |> requestOp c |> response |> parseCallRet

let batch (c : Client) (ops : Op seq) =
    let parseToken (token : JToken) = 
        let ok = token.["code"].ToString() |> Int32.Parse |> accepted
        let json = token.["data"].ToString()
        ok, json
    match requestBatch c ops |> response with
    | true, json -> 
        JArray.Parse(json) 
        |> Seq.map parseToken 
        |> Seq.zip ops 
        |> Seq.map parseOpItemRet
    | _, _ -> Seq.empty

let list (c : Client) (bucket : String) (limit : Int32) (prefix : String) (delimiter : String) (marker : String) =
    let query = 
        [| 
            String.Format("bucket={0}", bucket)
            (if limit = 0 then "" else String.Format("limit={0}", limit))
            (if nullOrEmpty prefix then "" else String.Format("limit={0}", limit))
            (if nullOrEmpty delimiter then "" else String.Format("delimiter={0}", delimiter))
            (if nullOrEmpty marker then "" else String.Format("marker={0}", marker))
        |] |> String.concat "&"
    String.Format("{0}/list?{1}", c.config.rsfHost, query)
    |> requestOp c |> response |> parseListRet