module QiniuFS.PFOP

open System
open System.IO
open System.Net
open Util
open Client
open FOP

type PFopSucc = {
    persistentId : String
}

type PFopExtra = {
    notifyURL : String
    force : Int32
    pipeline : String
}

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
        return! requestPFop c body |!> responseJson |>> parseJson Ret<PFopSucc>.Succ
    }
