module QiniuFS.RS

open System
open System.IO
open System.Net
open System.Text
open Newtonsoft.Json

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

type OpItem = {
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

let private rsHostGet<'succ> (c : Client) (op : Op) =
    String.Concat(c.config.rsHost, op |> opToUri)
    |> requestOp c
    |> responseJson<'succ>

let stat (c : Client) (en : Entry) =
    rsHostGet<StatSucc> c (OpStat en)

let delete (c : Client) (en : Entry) = 
    rsHostGet<Unit> c (OpDelete en)

let copy (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet<Unit> c (OpCopy (src, dst))

let move (c : Client) (src : Entry) (dst : Entry) =
    rsHostGet<Unit> c (OpMove (src, dst))

let fetch (c : Client) (url : String) (dst : Entry) =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.ioHost, "fetch", 
        Base64Safe.fromString url, "to", dst.Encoded) 
    |> requestOp c |> responseJson<FetchSucc>

let changeMime (c : Client) (mime : String) (en : Entry)  =
    String.Format("{0}/{1}/{2}/{3}/{4}", c.config.rsHost, "chgm",
        en.Encoded, "mime", Base64Safe.fromString mime) 
    |> requestOp c |> responseJson<Unit>

let private parseOpRet (op : Op, item : OpItem) =
    match op, accepted item.code with
    | _, false -> item.data |> jsonToObject<Error> |> Error
    | OpStat _, true -> item.data |> jsonToObject<StatSucc> |> StatSucc |> Succ
    | _ -> CallSucc () |> Succ

let batch (c : Client) (ops : Op[]) =
    let parse (data : Stream) =
        streamToString data
        |> jsonToObject<OpItem[]>
        |> Array.zip ops
        |> Array.map parseOpRet
    async {
        let buf = Array.zeroCreate (1 <<< 8) // 256B
        let data = new MemoryStream()
        let! req = requestBatch c (encodeOps ops)
        let! code = responseCopy buf req data
        data.Position <- 0L
        match accepted code with
        | true -> return parse data
        | false -> return Array.empty
    }
