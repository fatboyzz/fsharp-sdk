module QiniuFS.FOP

open System
open System.IO
open System.Net

type SaveAs = {
    entry : Entry
}

let private saveAsToUri (param : SaveAs) =
    String.Format("saveas/{0}", param.entry.Encoded)


type ImageView2 = {
    Mode : Int32
    W : Int32
    H : Int32
    Q : Int32
    Interlace : Int32
    Format : String
}

let imageView2 = Zero.instance<ImageView2>

let private imageView2ToUri (param : ImageView2) =
    seq {
        yield "imageView2/" + param.Mode.ToString()
        if param.W > 0 then yield "/w/" + param.W.ToString()
        if param.H > 0 then yield "/h/" + param.H.ToString()
        if param.Q > 0 then yield "/q/" + param.Q.ToString()
        if param.Interlace > 0 then yield "/interlace/" + param.Interlace.ToString()
        if nullOrEmpty param.Format |> not then yield "format" + param.Format
    } |> concat

let fopImageView2 (url : String) = 
    let buf = Array.zeroCreate (1 <<< 15) // 32K
    requestUrl url |> responseStream buf


let private imageInfoToUri = "imageInfo"

type ImageInfoSucc = {
    format : String
    width : Int32
    height : Int32
    colorModel : String
    frameNumber : Int32
}

let fopImageInfo (url : String) =
    requestUrl url |> responseJson<ImageInfoSucc>


type Fop =
| Tee of Fop[]
| Pipe of Fop[]
| Uri of String
| SaveAs of SaveAs
| ImageView2 of ImageView2
| ImageInfo

let rec fopToUri (fop : Fop) =
    seq {
        match fop with
        | Tee fops -> yield! fopsToUri ";" fops
        | Pipe fops -> yield! fopsToUri "|" fops
        | Uri uri -> yield uri
        | SaveAs param -> yield saveAsToUri param
        | ImageView2 param -> yield imageView2ToUri param
        | ImageInfo -> yield imageInfoToUri
    }

and private fopsToUri (sep : String) (fops : seq<Fop>) =
    Seq.map fopToUri fops 
    |> interpolate (seq { yield sep })
    |> Seq.concat
