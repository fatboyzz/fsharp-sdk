module QiniuFS.Base64Safe

open System
open Util

let private toUrl(c : Char) =
    match c with
    | '+' -> '-'
    | '/' -> '_'
    | _ -> c

let private fromUrl(c : Char) =
    match c with
    | '-' -> '+'
    | '_' -> '/'
    | _ -> c

let fromBytes (bs : byte[]) =
    Convert.ToBase64String(bs) |> String.map toUrl

let toBytes (s : String) =
    String.map fromUrl s |> Convert.FromBase64String

let fromString =
    stringToUtf8 >> fromBytes

let toString =
    toBytes >> utf8ToString
