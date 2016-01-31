module Qiniu.D

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

let down (url : String) (extra : DownExtra) (path : String) = 
    async {
        use output = File.OpenWrite(path)
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
    