module QiniuFS.IO

open System
open System.IO
open System.Net
open System.Collections.Generic
open Util
open Client
open FOP

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
| No = 0
| Auto = 1
| Check = 2

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

type Part =
    | KVPart of key : String * value : String
    | StreamPart of mime : String * input : Stream

let unix = DateTime(1970, 1, 1)

let scope (entry : Entry) = entry.Scope

let deadline (expire : TimeSpan) =
    ((DateTime.UtcNow + expire) - unix).Ticks / 10000000L |> uint32

let sign (c : Client) = c.mac.SignWithObject

let defaultExpire = TimeSpan.FromSeconds(3600.0) 
let defaultDeadline _ = deadline defaultExpire

let putPolicy = Zero.instance<PutPolicy>
let putExtra = { Zero.instance<PutExtra> with crc32 = -1 }

let private writePart (boundary : String) (output : Stream) (part : Part) =
    let wso = stringToUtf8 >> output.AsyncWrite
    let wsso = concatUtf8 >> output.AsyncWrite

    let dispositionLine (name : String) =
        [| 
            @"Content-Disposition: form-data; "
            (if nullOrEmpty name then "" else String.Format("name=\"{0}\";", name))
            crlf
        |] |> concat

    let contentTypeLine (mime : String) = 
        [|
            @"Content-Type: "
            (if nullOrEmpty mime then "application/octet-stream" else mime)
            crlf
        |] |> concat
    
    let writeKVPart (key : String) (value : String) =
        [| dispositionLine key; crlf; value |] |> wsso

    let writeStreamPart (mime : String) (input : Stream) =
        async {
            do! [|
                    dispositionLine "file"
                    contentTypeLine mime
                    crlf
                |] |> wsso
            let buf = Array.zeroCreate (2 <<< 15)
            do! asyncCopy buf input output
        }

    async {
        let boundaryLine = String.Format("{0}--{1}{2}", crlf, boundary, crlf)
        do! boundaryLine |> wso
        match part with
        | KVPart(key, value) -> do! writeKVPart key value
        | StreamPart(mime, input) -> do! writeStreamPart mime input
    }

let private writeParts (req : HttpWebRequest) (parts : Part seq) =
    async {
        let boundary = String.Format("{0:N}", Guid.NewGuid())
        let boundaryEnd = String.Format("{0}--{1}--{2}", crlf, boundary, crlf)
        req.Method <- "POST"
        req.ContentType <- "multipart/form-data; boundary=" + boundary
        use! output = requestStream req
        for part in parts do
            do! writePart boundary output part
        do! boundaryEnd |> stringToUtf8 |> output.AsyncWrite
    }

let private crcPart (extra : PutExtra) (input : Stream) =
    seq {   
        match extra.checkCrc with
        | CheckCrc.No -> ()
        | CheckCrc.Auto -> 
            let crc = CRC32.hashIEEE 0u input
            input.Position <- 0L
            yield KVPart("crc32", crc.ToString())
        | CheckCrc.Check -> 
            yield KVPart("crc32", extra.crc32.ToString())
        | _ -> failwith "Wrong extra.checkCrc"
    }

let private doput (param : PutParam) (input : Stream) =
    async {
        let check (k : String, _) = k.StartsWith("x:")
        let parts = 
            seq {
                let extra = param.extra
                yield KVPart("token", param.token)
                if nullOrEmpty param.key |> not then 
                    yield KVPart("key", param.key)
                yield! extra.customs |> Seq.filter check |> Seq.map KVPart
                yield! crcPart param.extra input
                yield StreamPart(extra.mimeType, input)
            }
        let req = requestUrl param.c.config.upHost
        do! parts |> writeParts req
        return! req |> responseJson |>> parseJson Ret<PutSucc>.Succ
    }

let put (c : Client) (token : String) (key : String) (input : Stream) (extra : PutExtra) =
    doput { c = c; token = token; key = key; extra = extra } input

let putFile (c : Client) (token : String) (key : String) (path : String) (extra : PutExtra) =
    async {
        use input = File.OpenRead(path)
        return! put c token key input extra
    }

let publicUrl (domain : String) (key : String) =
    String.Format("http://{0}/{1}", domain, Uri.EscapeUriString key)

let publicUrlFop (domain : String) (key : String) (fop : Fop) =
    seq {
        yield publicUrl domain key
        yield "?"
        yield! (fopToUri fop)
    } |> concat

let attachToken (c : Client) (url : String) =
    let token = url |> stringToUtf8 |> c.mac.Sign
    String.Format("{0}&token={1}", url, token)

let privateUrl (c : Client) (domain : String) (key : String) (deadline : Int32) =
    String.Format("{0}?e={1}", publicUrl domain key, deadline) 
    |> attachToken c

let privateUrlFop (c : Client) (domain : String) (key : String) (fop : Fop) (deadline : Int32) =
    String.Format("{0}&e={1}", publicUrlFop domain key fop, deadline) 
    |> attachToken c
    