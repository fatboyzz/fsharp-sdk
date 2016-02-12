module QiniuFS.RD

open System
open System.IO
open System.Net
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

type private BlockCtx = {
    blockId : Int32
    blockSize : Int32
    prev : ChunkSucc
    writeAt : Int64 -> byte[] -> unit
    notify : Progress -> unit
}

type private ContentRange = {
    first : Int64
    last : Int64
    complete : Int64
}
    
let rdownExtra = {
    blockSize = 1 <<< 22 // 4M
    chunkSize = 1 <<< 21 // 2M
    bufSize = 1 <<< 15 // 32K
    tryTimes = 3
    worker = 2
    progresses = Array.empty
    notify = ignore
}

let private acceptRange (resp : HttpWebResponse) =
    let s = resp.Headers.[HttpResponseHeader.AcceptRanges]
    if (nullOrEmpty s) then false else s = "bytes"

#if NET20
let private add = 
    typeof<WebHeaderCollection>.GetMethod("AddWithoutValidate", 
        Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)    
#endif

let private addRange (req : HttpWebRequest) (first : Int64) (last : Int64) = 
#if NET20
    add.Invoke(req.Headers, [| "Range"; String.Format("bytes={0}-{1}", first, last) |]) |> ignore
#else
    req.AddRange(first, last)
#endif

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
        let req = requestUrl param.url
        req.Method <- "GET"
        let first = blockStart + int64 offset
        let last = first + int64 length - 1L
        addRange req first last
        req

    let down (offset : Int32) =
        async {
            let reqLength = min extra.chunkSize (ctx.blockSize - offset)
            let req = requestRange offset reqLength
            let data = new MemoryStream()
            let! code = responseCopy (buf.Force()) req data
            match accepted code with
            | true ->
                ctx.writeAt (blockStart + int64 offset) (data.ToArray())
                let next = { offset = offset + int32 data.Length }
                ctx.notify { blockId = ctx.blockId; blockSize = ctx.blockSize; ret = next }
                return Succ next
            | false ->
                return Error { error = String.Format("Response chunk with status code {0}", code) }
        }

    let rec loop (times : Int32) (prev : ChunkSucc) (cur : Ret<ChunkSucc>) =
        async {
            match times < extra.tryTimes, cur with
            | true, Succ c when c.offset = ctx.blockSize ->
                return cur 
            | true, Succ c ->
                return! down c.offset |!> loop times c
            | true, Error _ ->
                return! loop (times + 1) prev (Succ prev)
            | false, _ -> 
                return cur
        }

    loop 0 ctx.prev (Succ ctx.prev)

let private doRDown (param : RDownParam) (output : Stream) =
    let extra = param.extra
    let blockSize = extra.blockSize
    let blockCount = int32 ((param.length + int64 blockSize - 1L) / int64 blockSize)
    let blockLast = int32 (param.length - int64 blockSize * int64 (blockCount - 1))
    let blockSizeOfId (blockId : Int32) =
        if blockId = blockCount - 1 then blockLast else blockSize

    let revProgresses = extra.progresses |> Array.rev
    let writeAt = writerAt output
        
    let notifyLock = new Object()
    let notify (p : Progress) =
        lock notifyLock (fun _ -> extra.notify p)

    let work (blockId : Int32) =
        async {
            let blockCtx (prev : ChunkSucc) = { 
                blockId = blockId; blockSize = blockSizeOfId blockId; 
                prev = prev; writeAt = writeAt; notify = notify 
            }
            let progress = revProgresses |> Array.tryFind (fun p -> p.blockId = blockId) 
            match progress with
            | Some p -> return! p.ret |> blockCtx |> block param 
            | None -> return! { offset = 0 } |> blockCtx |> block param 
        }
    
    async {
        let! rets = [| 0 .. blockCount - 1 |]
                    |> Array.map work
                    |> limitedParallel extra.worker
        if Array.forall checkRet rets 
        then return Succ ()
        else return Error { error = "Block not all done" }
    }

let private responseDummy (url : String) =
    async {
        let req = requestUrl url
        addRange req 0L 0L
        use! resp = responseCatched req 
        match accepted resp.StatusCode, acceptRange resp with
        | true, true -> 
            return parseContentRange resp |> Succ
        | true, false -> 
            return Error { error = "Response do not have header AcceptRanges : bytes" } 
        | false, _ -> 
            return Error { error = String.Format("Response dummy with status code {0}", resp.StatusCode) } 
    }

let rdown (url : String) (extra : RDownExtra) (output : Stream) = 
    async {
        let! ret = responseDummy url
        match ret with
        | Succ cr -> return! doRDown { url = url; extra = extra; length = cr.complete } output
        | Error e -> return Error e
    }

let rdownFile (url : String) (extra : RDownExtra) (path : String) =
    async {
        use output = File.OpenWrite(path)
        return! rdown url extra output
    }
