module QiniuFS.D

open System
open System.IO
open System.Net
open Util
open Client

type DownExtra = {
    bufSize : Int32
}

let downExtra = { 
    bufSize = 1 <<< 15 // 32K
}

let parseDown (code : HttpStatusCode) =
    match accepted code with
    | true -> Ret<Unit>.Succ ()
    | false -> Error { error = String.Format("Response down with status code {0}", code) }

let down (url : String) (extra : DownExtra) (output : Stream) = 
    async {
        let buf : byte[] = Array.zeroCreate extra.bufSize
        let req = requestUrl url
        req.Method <- "GET"
        let! code = responseCopy buf req output
        return parseDown code
    }
    
let downFile (url : String) (extra : DownExtra) (path : String) =
    async {
        use output = File.OpenWrite(path)
        return! down url extra output
    }