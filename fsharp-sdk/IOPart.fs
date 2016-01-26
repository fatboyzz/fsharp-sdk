module Qiniu.IOPart

open System
open System.Text
open System.IO
open System.Net
open System.Collections.Generic
open Util
open Client

type Part =
    | KVPart of key : String * value : String
    | StreamPart of mime : String * input : Stream

let writePart (boundary : String) (output : Stream) (part : Part) =
    let wso = stringToUtf8 >> output.AsyncWrite
    let wsso = concat >> wso

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

let writeRequest (req : HttpWebRequest) (parts : Part seq) =
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
    