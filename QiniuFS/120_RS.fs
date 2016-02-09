module QiniuFS.RS

open System
open System.IO
open System.Net
open System.Text
open Newtonsoft.Json
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

type Op = 
    | OpStat of Entry
    | OpDelete of Entry
    | OpCopy of src : Entry * dst : Entry
    | OpMove of src : Entry * dst : Entry

type OpSucc = 
| CallSucc of Unit
| StatSucc of StatSucc

type OpItemSucc = {
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

let internal requestOp (c : Client) (url : String) =
    let req = requestUrl url
    req.Method <- "GET"
    req.ContentType <- "application/x-www-form-urlencoded"
    req.Headers.Add(HttpRequestHeader.Authorization, authorization c (req, null))
    req

let private requestBatch (c : Client) (body : byte[]) =
    async {
        let url = String.Format("{0}/batch", c.config.rsHost)
        let req = requestUrl url
        req.Method <- "POST"
        req.ContentType <- "application/x-www-form-urlencoded"
        req.Headers.Add(HttpRequestHeader.Authorization, authorization c (req, body))
        let! output = requestStream req
        do! output.AsyncWrite(body, 0, body.Length)
        return req
    }

let private parseCallRet = parseJson (fun _ -> Succ())

let private parseOpRet (op : Op, item : OpItemSucc) =
    match op, item.code |> accepted with
    | _, false -> item.data |> jsonToObject<Error> |> Error
    | OpStat _, _ -> item.data |> jsonToObject<StatSucc> |> StatSucc |> Succ
    | _ -> CallSucc () |> Succ

let private rsHostGet (c : Client) (op : Op) =
    String.Concat(c.config.rsHost, op |> opToUri)
    |> requestOp c
    |> responseJson

let stat (c : Client) (en : Entry) =
    rsHostGet c (OpStat en) |>> parseJson Ret<StatSucc>.Succ

let delete (c : Client) (en : Entry) = 
    rsHostGet c (OpDelete en) |>> parseCallRet

let copy (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet c (OpCopy (src, dst)) |>> parseCallRet

let move (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet c (OpMove (src, dst)) |>> parseCallRet

let fetch (c : Client) (url : String) (dst : Entry) =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.ioHost, "fetch", 
        Base64Safe.fromString url, "to", dst.Encoded) 
    |> requestOp c |> responseJson |>> parseJson Ret<FetchSucc>.Succ

let changeMime (c : Client) (mime : String) (en : Entry)  =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.rsHost, "chgm",
        en.Encoded, "mime", Base64Safe.fromString mime) 
    |> requestOp c |> responseJson |>> parseCallRet

let batch (c : Client) (ops : Op[]) =
    let parse (code : HttpStatusCode, json : String) =
        match accepted code, json with
        | true, _ -> 
            json 
            |> jsonToObject<OpItemSucc[]>
            |> Array.zip ops
            |> Array.map parseOpRet
        | false, _ -> Array.empty
    async {
        let body = encodeOps ops
        return! requestBatch c body |!> responseJson |>> parse
    }
    