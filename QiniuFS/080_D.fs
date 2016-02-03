module QiniuFS.D

open System
open System.IO
open Util
open Client

type DownRet = 
| DownSucc
| DownError of Error

type DownExtra = {
    bufSize : Int32
}

let downExtra = { 
    bufSize = 1 <<< 15 // 32K
}

let checkDownRet (ret : DownRet) =
    match ret with
    | DownSucc -> ()
    | DownError e -> failwith e.error

let down (url : String) (extra : DownExtra) (output : Stream) = 
    async {
        let buf : byte[] = Array.zeroCreate extra.bufSize
        let req = request url
        req.Method <- "GET"
        use! resp = responseCatched req
        match accepted resp.StatusCode with
        | true ->
            use input = resp.GetResponseStream()
            do! asyncCopy buf input output
            return DownSucc
        | false ->
            let error = String.Format("Error StatusCode {0}", resp.StatusCode)
            return DownError { error = error }
    }
    
let downFile (url : String) (extra : DownExtra) (path : String) =
    async {
        use output = File.OpenWrite(path)
        return! down url extra output
    }