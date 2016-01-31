module Qiniu.Base64Safe

open System

let toUrl(c : Char) =
    match c with
    | '+' -> '-'
    | '/' -> '_'
    | _ -> c

let fromUrl(c : Char) =
    match c with
    | '-' -> '+'
    | '_' -> '/'
    | _ -> c

let encode(bs : byte[]) : String =
    Convert.ToBase64String(bs) |> String.map toUrl

let decode(s : String) : byte[] =
    String.map fromUrl s |> Convert.FromBase64String
