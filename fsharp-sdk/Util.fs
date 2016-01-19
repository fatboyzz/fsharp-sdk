module Qiniu.Util

open System
open System.IO
open System.Text
open System.Net
open Newtonsoft.Json

let stringToUtf8 (s : String) =
    Encoding.UTF8.GetBytes(s)

let stringToBase64Safe (s : String) =
    s |> stringToUtf8 |> Base64Safe.encode

let utf8ToString (bs : byte[]) =
    Encoding.UTF8.GetString(bs)

let base64SafeToString (s : String) =
    s |> Base64Safe.decode |> utf8ToString

let streamToBytes (stream : Stream) =
    use ms = new MemoryStream()
    stream.CopyTo(ms)
    ms.GetBuffer()

let streamToString (stream : Stream) =
    use reader = new StreamReader(stream, Encoding.UTF8)
    reader.ReadToEnd()

let stringToStream (s : String) =
    new MemoryStream(stringToUtf8 s)

let jsonSettings =
    new JsonSerializerSettings(
        NullValueHandling = NullValueHandling.Ignore
    )

let jsonToObject<'T>(s : String) : 'T =
    JsonConvert.DeserializeObject<'T>(s, jsonSettings)

let objectToJson<'T>(o : 'T) =
    JsonConvert.SerializeObject(o, jsonSettings)

let objectToJsonIndented<'T>(o : 'T) =
    JsonConvert.SerializeObject(o, Formatting.Indented, jsonSettings)

let writeBytes (s : Stream) (bs : byte[]) =
    s.Write(bs, 0, bs.Length)

let writeByte (s : Stream) (b : byte) =
    s.WriteByte b

let concat (ss : String seq) = 
    String.Concat ss

let join (sep : String) (ss : String seq) =
    String.Join(sep, Seq.toArray ss)

let crlf = "\r\n"

let nullOrEmpty = String.IsNullOrEmpty

let headers (req : HttpWebRequest) =
    let hs = req.Headers
    hs.AllKeys |> Array.map (fun (key) -> (key, hs.[key]))

let constf v = (fun _ -> v)

type FSharpType = Microsoft.FSharp.Reflection.FSharpType
type FSharpValue = Microsoft.FSharp.Reflection.FSharpValue

let isClass (t : Type) = t.IsClass
let isValue (t : Type) = t.IsValueType
let isRecord = FSharpType.IsRecord
let isTuple = FSharpType.IsTuple
let isArray (t : Type) = t.IsArray

let rec private zeroInstance (t : Type) =
    match t with
    | _ when isValue t -> Activator.CreateInstance t
    | _ when isRecord t -> zeroRecord t
    | _ when isTuple t -> zeroTuple t
    | _ when isArray t -> zeroArray t
    | _ -> null

and private zeroRecord (t : Type) =
    FSharpType.GetRecordFields t
    |> Array.map (fun p -> zeroInstance p.PropertyType)
    |> (fun objs -> FSharpValue.MakeRecord (t, objs))

and private zeroTuple (t : Type) =
    FSharpType.GetTupleElements t
    |> Array.map zeroInstance
    |> (fun objs -> FSharpValue.MakeTuple (objs, t))

and private zeroArray (t : Type) =
    Array.CreateInstance(t.GetElementType(), 0) :> Object

let zero<'a> : 'a = zeroInstance typeof<'a> :?> 'a
        

