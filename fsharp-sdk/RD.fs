module Qiniu.RD

open System
open System.IO
open System.Net
open Util
open Client
open D

type ChunkSucc = {
    offset : Int32
}

type Progress = {
    blockId : Int32
    blockSize : Int32
    ret : ChunkSucc
}

type RDownExtra = {
    blockSize : Int32
    chunkSize : Int32
    bufSize : Int32
    tryTimes : Int32
    worker : Int32
    progresses : Progress[]
    notify : Progress -> unit
}

type private RDownParam = {
    url : String
    extra : RDownExtra
    length : Int64
}

type private ChunkRet =
| ChunkSucc of ChunkSucc
| ChunkError of code : HttpStatusCode

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
    
let rdownExtra = {
    zero<RDownExtra> with 
        blockSize = 1 <<< 22 // 4M
        chunkSize = 1 <<< 20 // 1M
        bufSize = 1 <<< 15 // 32K
        tryTimes = 3
        worker = 2
        notify = ignore
}

let private acceptRange (resp : HttpWebResponse) =
    let s = resp.Headers.[HttpResponseHeader.AcceptRanges]
    if (nullOrEmpty s) then false else s = "bytes"

let private addRange (req : HttpWebRequest) (first : Int64) (last : Int64) =  
    req.AddRange(first, last)

let private parseContentRange (resp : HttpWebResponse) =
    let s = resp.Headers.[HttpResponseHeader.ContentRange]
    let ss = s.Split([| ' '; '-'; '/' |], 4)
    { first = int64 ss.[1]; last = int64 ss.[2]; complete = int64 ss.[3] }

let private n = ref 0

let private block (param : RDownParam) (ctx : BlockCtx) =
    let extra = param.extra
    let blockStart = int64 ctx.blockId * int64 extra.blockSize
    let buf = lazy Array.zeroCreate extra.bufSize

    let requestRange (offset : Int32) (length : Int32) =
        async {
            let req = request param.url
            req.Method <- "GET"
            let first = blockStart + int64 offset
            let last = first + int64 length - 1L
            addRange req first last
            return req
        }

    let down (offset : Int32) =
        async {
            let reqLength = min extra.chunkSize (ctx.blockSize - offset)
            let! req = requestRange offset reqLength
            use! resp = responseCatched req
            match accepted resp.StatusCode with
            | true ->
                let cr = parseContentRange resp
                let respLength = int32 (cr.last - cr.first) + 1
                use input = resp.GetResponseStream()
                use output = new MemoryStream(respLength)
                let! data = asyncCopy (buf.Force()) input output
                ctx.writeAt (blockStart + int64 offset) (output.ToArray())
                let next = { offset = offset + respLength }
                ctx.notify { blockId = ctx.blockId; blockSize = ctx.blockSize; ret = next }
                return ChunkSucc next
            | false ->
                return ChunkError resp.StatusCode
        }

    let rec loop (times : Int32) (prev : ChunkRet) =
        match times < extra.tryTimes, prev with
        | true, ChunkSucc succ when succ.offset = ctx.blockSize ->
            async { return prev }
        | true, ChunkSucc succ ->
            down succ.offset |!> loop 0
        | true, ChunkError code ->
            loop (times + 1) prev
        | false, _ -> async { return prev }

    loop 0 ctx.prev

let private doRDown (param : RDownParam) (output : FileStream) =
    let extra = param.extra
    let blockSize = extra.blockSize
    let blockCount = int32 ((param.length + int64 blockSize - 1L) / int64 blockSize)
    let blockLast = int32 (param.length - int64 blockSize * int64 (blockCount - 1))
    let blockSizeOfId (blockId : Int32) =
        if blockId = blockCount - 1 then blockLast else blockSize

    let revProgresses = extra.progresses |> Array.rev
        
    let outputLock = new Object()
    let writeAt (blockId : Int32) (offset : Int64) (data : byte[]) =
        lock outputLock (fun _ -> 
            output.Position <- offset
            output.Write(data, 0, data.Length))
        
    let notifyLock = new Object()
    let notify (p : Progress) =
        lock notifyLock (fun _ -> extra.notify p)

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
                    |> limitedParallel extra.worker
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

let rdown (url : String) (extra : RDownExtra) (path : String) = 
    async {
        let! ret = requestDummy url
        match ret with
        | DummySucc cr -> 
            use output = File.OpenWrite path
            return! doRDown { url = url; extra = extra; length = cr.complete } output
        | DummyError error -> 
            return error |> DownError
    }
