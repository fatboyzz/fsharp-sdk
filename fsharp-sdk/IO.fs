module Qiniu.IO

open System
open System.IO
open System.Net
open System.Collections.Generic
open Util
open Client
open IOPart

type PutPolicy = {
    scope : String
    deadline : UInt32
    insertOnly : UInt16
    detectMime : Byte
    callbackFetchKey : Byte
    fsizeLimit : Int64
    mimeLimit : String
    saveKey : String
    callbackUrl : String
    callbackHost : String
    callbackBody : String
    callbackBodyType : String
    returnUrl : String
    returnBody : String
    persistentOps : String
    persistentNotifyUrl : String
    persistentPipeline : String
    asyncOps : String
    endUser : String
    checkSum : String
}

type CheckCrc = 
    | DEFAULT_CHECK = -1
    | NO_CHECK = 0
    | CHECK_AUTO = 1
    | CHECK = 2

type PutExtra = {
    customs : (String * String)[]
    crc32 : Int32
    checkCrc : CheckCrc
    mimeType : String
}

type private PutParam = {
    c : Client
    token : String
    key : String
    extra : PutExtra
}

type PutSucc = {
    hash : String
    key : String
}

type PutRet = 
| PutSucc of PutSucc 
| PutError of Error

let unix = DateTime(1970, 1, 1)

let scope (entry : Entry) = entry.Scope

let deadline (expire : TimeSpan) =
    ((DateTime.UtcNow + expire) - unix).Ticks / 10000000L |> uint32

let sign (c : Client) = c.mac.SignWithObject

let defaultExpire = TimeSpan.FromSeconds(3600.0) 
let defaultDeadline _ = deadline defaultExpire

let putPolicy = zero<PutPolicy>
let putExtra = { zero<PutExtra> with crc32 = -1 }

let parsePutRet = parse PutSucc PutError

let private doput (param : PutParam) (input : Stream) =
    let check (k : String, _) = k.StartsWith("x:")
    let parts _ = 
        seq {
            let extra = param.extra
            yield KVPart("token", param.token)
            if nullOrEmpty param.key |> not then 
                yield KVPart("key", param.key)
            yield! extra.customs |> Seq.filter check |> Seq.map KVPart
            if extra.checkCrc <> CheckCrc.NO_CHECK then 
                yield KVPart("crc32", extra.crc32.ToString())
            yield StreamPart(extra.mimeType, input)
        }
    async {
        let req = request param.c.config.upHost
        do! writeRequest req <| parts()
        return! req |> responseJson |>> parsePutRet
    }

let put (c : Client) (token : String) (key : String) (input : Stream) (extra : PutExtra) =
    doput { c = c; token = token; key = key; extra = extra } input

let publicUrl (domain : String) (key : String) =
    String.Format("http://{0}/{1}", domain, Uri.EscapeUriString key)

let privateUrl (c : Client) (domain : String) (key : String) (deadline : Int32) =
    let url = String.Format("{0}?e={1}", publicUrl domain key, deadline)
    let token = url |> stringToUtf8 |> c.mac.Sign
    String.Format("{0}&token={1}", url, token)
