module QiniuFS.Zero

open System

type FSharpType = Microsoft.FSharp.Reflection.FSharpType
type FSharpValue = Microsoft.FSharp.Reflection.FSharpValue

let inline isClass (t : Type) = t.IsClass
let inline isValue (t : Type) = t.IsValueType
let inline isRecord (t : Type) = FSharpType.IsRecord t
let inline isTuple (t : Type) = FSharpType.IsTuple t
let inline isArray (t : Type) = t.IsArray

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

let instance<'a> : 'a = zeroInstance typeof<'a> :?> 'a

