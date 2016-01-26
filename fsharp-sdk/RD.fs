module Qiniu.RD

open System
open System.IO
open System.Net
open Qiniu
open Qiniu.Util
open Qiniu.Client

type ChunkSucc = {
    offset : Int32
}

type ChunkRet =
| ChunkSucc of ChunkSucc
| ChunkError of code : HttpStatusCode

type Progress = {
    blockId : Int32
    blockSize : Int32
    ret : ChunkSucc
}

type DownExtra = {
    blockSize : Int32
    chunkSize : Int32
    tryTimes : Int32
    worker : Int32
    progresses : Progress[]
    notify : Progress -> unit
}

type private DownParam = {
    url : String
    extra : DownExtra
    length : Int64
}

type DownRet = 
| DownSucc
| DownError of Error

type private BlockCtx = {
    blockId : Int32
    blockSize : Int32
    prev : ChunkRet
    writeAt : Int64 -> byte[] -> unit
    notify : Progress -> unit
}

type private ContentRange = {
    first : Int64
    last : Int64
    complete : Int64
}
    
let downExtra = {
    zero<DownExtra> with 
        blockSize = 1 <<< 22 // 4M
        chunkSize = 1 <<< 20 // 1M
        tryTimes = 3
        worker = 4
        notify = ignore
}

let private acceptRange (resp : HttpWebResponse) =
    let s = resp.Headers.[HttpResponseHeader.AcceptRanges]
    if (nullOrEmpty s) then false else s = "bytes"

let addRange (req : HttpWebRequest) (first : Int64) (last : Int64) =  
    req.AddRange(first, last)

let private parseContentRange (resp : HttpWebResponse) =
    let s = resp.Headers.[HttpResponseHeader.ContentRange]
    let ss = s.Split([| ' '; '-'; '/' |], 4)
    { first = int64 ss.[1]; last = int64 ss.[2]; complete = int64 ss.[3] }

let private n = ref 0

let private block (param : DownParam) (ctx : BlockCtx) =
    let extra = param.extra
    let blockStart = int64 ctx.blockId * int64 extra.blockSize
    let blockSize = ctx.blockSize
    let chunkSize = extra.chunkSize
    let downBuf = lazy Array.zeroCreate chunkSize

    let requestRange (offset : Int32) (length : Int32) =
        async {
            let req = request param.url
            req.Method <- "POST"
            let first = blockStart + int64 offset
            let last = first + int64 length - 1L
            addRange req first last
            use! stream = requestStream req
            return req
        }

    let down (offset : Int32) =
        async {
            let reqLength = min chunkSize (blockSize - offset)
            let! req = requestRange offset reqLength
            use! resp = responseCatched req
            match accepted resp.StatusCode with
            | true ->
                let cr = parseContentRange resp
                let respLength = int32 (cr.last - cr.first) + 1
                use input = resp.GetResponseStream()
                let! data = asyncReadAll (downBuf.Force()) input
                ctx.writeAt (blockStart + int64 offset) data
                let next = { offset = offset + respLength }
                ctx.notify { blockId = ctx.blockId; blockSize = ctx.blockSize; ret = next }
                return ChunkSucc next
            | false ->
                return ChunkError resp.StatusCode
        }

    let rec loop (times : Int32) (prev : ChunkRet) =
        match times < extra.tryTimes, prev with
        | true, ChunkSucc succ when succ.offset = blockSize ->
            async { return prev }
        | true, ChunkSucc succ ->
            down succ.offset |!> loop 0
        | true, ChunkError code ->
            loop (times + 1) prev
        | false, _ -> async { return prev }

    loop 0 ctx.prev

let private doDown (param : DownParam) (output : FileStream) =
    let blockSize = param.extra.blockSize
    let blockCount = int32 ((param.length + int64 blockSize - 1L) / int64 blockSize)
    let blockLast = int32 (param.length - int64 blockSize * int64 (blockCount - 1))
    let blockSizeOfId (blockId : Int32) =
        if blockId = blockCount - 1 then blockLast else blockSize

    let revProgresses = param.extra.progresses |> Array.rev
        
    let outputLock = new Object()
    let writeAt (blockId : Int32) (offset : Int64) (data : byte[]) =
        lock outputLock (fun _ -> 
            output.Position <- offset
            output.Write(data, 0, data.Length))
        
    let notifyLock = new Object()
    let notify (p : Progress) =
        lock notifyLock (fun _ -> param.extra.notify p)

    let work (blockId : Int32) =
        async {
            let blockCtx (prev : ChunkRet) = { 
                blockId = blockId; blockSize = blockSizeOfId blockId; 
                prev = prev; writeAt = writeAt blockId; notify = notify 
            }
            let progress = revProgresses |> Array.tryFind (fun p -> p.blockId = blockId) 
            match progress with
            | Some p -> return! ChunkSucc p.ret |> blockCtx |> block param 
            | None -> return! ChunkSucc { offset = 0 } |> blockCtx |> block param 
        }

    let check (ret : ChunkRet) =
        match ret with
        | ChunkSucc succ -> true
        | _ -> false
        
    async {
        let! rets = [| 0 .. blockCount - 1 |]
                    |> Array.map work
                    |> limitedParallel param.extra.worker
        if Array.forall check rets then return DownSucc
        else return DownError({ error = "Block not all done" }) 
    }

type private DummyRet =
    | DummySucc of ContentRange
    | DummyError of Error

let private requestDummy (url : String) =
    async {
        let req = request url
        addRange req 0L 0L
        use! resp = responseCatched req
        match accepted resp.StatusCode, acceptRange resp with
        | true, true -> return parseContentRange resp |> DummySucc
        | true, false -> 
            let error = "Response do not have header AcceptRanges : bytes"
            return { error = error } |> DummyError
        | false, _ -> 
            let error = String.Format("Error StatusCode {0}", resp.StatusCode)
            return { error = error } |> DummyError
    }

let down (url : String) (extra : DownExtra) (path : String) = 
    async {
        let! ret = requestDummy url
        match ret with
        | DummySucc cr -> 
            use output = File.OpenWrite path
            return! doDown { url = url; extra = extra; length = cr.complete } output
        | DummyError error -> 
            return error |> DownError
    }
