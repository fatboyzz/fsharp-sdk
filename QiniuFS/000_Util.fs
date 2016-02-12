[<AutoOpen>]
module QiniuFS.Util

open System
open System.IO
open System.Text
open System.Net
open System.Threading
open System.Collections.Generic
open Newtonsoft.Json

let inline unref (r : 'a ref) = !r

let inline stringToUtf8 (s : String) =
    Encoding.UTF8.GetBytes(s)

let inline utf8ToString (bs : byte[]) =
    Encoding.UTF8.GetString(bs)

let inline streamToString (stream : Stream) =
    let reader = new StreamReader(stream)
    reader.ReadToEnd()

let inline stringToStream (s : String) =
    new MemoryStream(stringToUtf8 s)

let jsonSettings =
    new JsonSerializerSettings(
        NullValueHandling = NullValueHandling.Ignore
    )

let inline jsonToObject<'a>(s : String) : 'a =
    JsonConvert.DeserializeObject<'a>(s, jsonSettings)

let inline objectToJson<'a>(o : 'a) =
    JsonConvert.SerializeObject(o, jsonSettings)

let inline objectToJsonIndented<'a>(o : 'a) =
    JsonConvert.SerializeObject(o, Formatting.Indented, jsonSettings)

let readJsons<'a>(input : Stream) =
    seq {
        let r = new StreamReader(input)
        let jr = new JsonTextReader(r)
        jr.SupportMultipleContent <- true
        let js = JsonSerializer.Create(jsonSettings)
        while jr.Read() do yield js.Deserialize<'a>(jr)
    }

let writeJson (output : Stream) (o : 'a) =
    let w = new StreamWriter(output)
    let jw = new JsonTextWriter(w)
    let js = JsonSerializer.Create(jsonSettings)
    js.Serialize(jw, o)
    jw.Flush()

let writeJsons (output : Stream) (os : 'a seq) = 
    let w = new StreamWriter(output)
    let jw = new JsonTextWriter(w)
    let js = JsonSerializer.Create(jsonSettings)
    for o in os do js.Serialize(jw, o)
    jw.Flush()

let interpolate (sep : 'a) (ss : seq<'a>) =
    seq {
        let iter = ss.GetEnumerator()
        if iter.MoveNext() then
            yield iter.Current
            while iter.MoveNext() do
                yield sep
                yield iter.Current
    }

let inline consSeq (car : 'a) (cdr : seq<'a>) =
    seq { yield car; yield! cdr }

let concat (ss : seq<String>) = 
    let c = Seq.sumBy String.length ss
    let sb = new StringBuilder(c)
    for s in ss do sb.Append(s) |> ignore
    sb.ToString()

let concatUtf8 (ss : seq<String>) =
    let output = new MemoryStream()
    let writer = new StreamWriter(output)
    using writer (fun w -> for s in ss do writer.Write(s))
    output.ToArray()

let concatBytes (bss : seq<byte[]>) =
    let c = Seq.sumBy Array.length bss
    let output = new MemoryStream(c)
    using output (fun o -> for bs in bss do o.Write(bs, 0, bs.Length))
    output.ToArray()

let crlf = "\r\n"
let inline nullOrEmpty (s : String) = String.IsNullOrEmpty s
let inline notNullOrEmpty (s : String) = s |> nullOrEmpty |> not

let readerAt (input : Stream) =
    let inputLock = new Object()
    fun (offset : Int64) (length : Int32) ->
        let buf : byte[] = Array.zeroCreate length
        lock inputLock (fun _ ->
            input.Position <- offset
            let count = input.Read(buf, 0, length)
            new MemoryStream(buf, 0, count)
        )

let writerAt (output : Stream) =
    let outputLock = new Object()
    fun (offset : Int64) (data : byte[]) ->
        lock outputLock (fun _ -> 
            output.Position <- offset
            output.Write(data, 0, data.Length)
        )

let inline (|>>) (computaion : Async<'a>) (con : 'a -> 'b) =
    async.Bind(computaion, con >> async.Return)

let inline (|!>) (computaion : Async<'a>) (con : 'a -> Async<'b>) =
    async.Bind(computaion, con >> async.ReturnFrom)

let asyncCopy (buf : byte[]) (src : Stream) (dst : Stream) =
    let l = buf.Length
    let rec loop _ =
        async {
            let! n = src.AsyncRead(buf, 0, l)
            if n > 0 then 
                do! dst.AsyncWrite(buf, 0, n)
                return! loop()
        }
    loop()

let limitedParallel (limit : Int32) (jobs : Async<'a>[]) =
    async {
        let length = jobs.Length
        let count = ref -1
        let rets : 'a[] = Array.zeroCreate length
        let rec worker wid =
            async {
                let index = Interlocked.Increment count
                if index < length then
                    let! ret = jobs.[index]
                    rets.[index] <- ret
                    do! worker wid
            }
        do! Array.init limit worker 
            |> Async.Parallel 
            |> Async.Ignore
        return rets
    }
    