module QiniuFS.PFOP

open System
open System.IO
open System.Net
open Newtonsoft.Json
open FOP

type PFopSucc = {
    persistentId : String
}

type PFopExtra = {
    notifyURL : String
    force : Int32
    pipeline : String
}

let pfopExtra = Zero.instance<PFopExtra>

let private requestPFop (c : Client) (body : byte[]) =
    async {
        let url = String.Format("{0}/pfop/", c.config.apiHost)
        let req = requestUrl url
        req.Method <- "POST"
        req.ContentType <- "application/x-www-form-urlencoded"
        req.Headers.Add(HttpRequestHeader.Authorization, authorization c (req, body))
        let! output = requestStream req
        do! output.AsyncWrite(body, 0, body.Length)
        return req
    }

let private pfopBody (en : Entry) (fop : Fop) (extra : PFopExtra) =
    seq {
        yield "bucket=" + en.Bucket
        yield "key=" + en.Key
        yield consSeq "fops=" (fopToUri fop) |> concat
        if nullOrEmpty extra.notifyURL |> not then
            yield "notifyURL=" + extra.notifyURL
        if extra.force > 0 then
            yield "force=" + extra.force.ToString()
        if nullOrEmpty extra.pipeline |> not then
            yield "pipeline=" + extra.pipeline
    } |> interpolate "&" |> concatUtf8

let pfop (c : Client) (en : Entry) (fop : Fop) (extra : PFopExtra) =
    async {
        let body = pfopBody en fop extra
        return! requestPFop c body |!> responseJson<PFopSucc>
    }

type PrefopCode = 
| Success = 0
| Wait = 1
| Processing = 2
| Fail = 3
| NotifyFail = 4

type PrefopItem = {
    cmd : String
    code : PrefopCode
    desc : String
    error : String
    hash : String
    key : String
    returnOld : Int32
}

type PrefopSucc = {
    id : String
    code : PrefopCode
    desc : String
    inputKey : String
    inputBucket : String
    items : PrefopItem[]
    pipeline : String
    reqid : String
}

let prefop (c : Client) (persistentId : String) =
    String.Format("{0}/status/get/prefop?id={1}", c.config.apiHost, persistentId)
    |> requestUrl |> responseJson<PrefopSucc>
