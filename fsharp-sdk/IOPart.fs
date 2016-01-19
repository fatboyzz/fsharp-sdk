module Qiniu.IOPart

open System
open System.Text
open System.IO
open System.Net
open System.Collections.Generic
open Util

type Part =
    | KVPart of key : String * value : String
    | StreamPart of mime : String * stream : Stream

let writeParts (output : Stream) (boundary : String) =
    let wso = stringToUtf8 >> writeBytes output
    let wsso = concat >> wso
    let boundaryLine = String.Format("{0}--{1}{2}", crlf, boundary, crlf)
    let boundaryEnd = String.Format("{0}--{1}--{2}", crlf, boundary, crlf)

    let writePart (part : Part) =
        let dispositionLine (name : String) =
            [| 
                "Content-Disposition: form-data; "
                (if nullOrEmpty name then "" else String.Format("name=\"{0}\";", name))
                crlf
            |] |> concat

        let contentTypeLine (mime : String) = 
            [|
                "Content-Type: "
                (if nullOrEmpty mime then "application/octet-stream" else mime)
                crlf
            |] |> concat
    
        let writeKVPart (key : String) (value : String) =
            [| dispositionLine key; crlf; value |] |> wsso

        let writeStreamPart (mime : String) (input : Stream) =
            dispositionLine "file" |> wso
            contentTypeLine mime |> wso
            crlf |> wso
            input.CopyTo output

        boundaryLine |> wso
        match part with
        | KVPart(key, value) -> writeKVPart key value
        | StreamPart(mime, input) -> writeStreamPart mime input

    Seq.map writePart >> Seq.toArray >> (fun _ -> wso boundaryEnd)

let writeRequest (req : HttpWebRequest) (parts : Part seq) =
    req.Method <- "POST"
    let boundary = String.Format("{0:N}", Guid.NewGuid())
    req.ContentType <- "multipart/form-data; boundary=" + boundary
    let reqStream = req.GetRequestStream()
    writeParts reqStream boundary parts
    