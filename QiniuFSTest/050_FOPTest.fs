namespace QiniuFSTest

open System
open System.IO
open NUnit.Framework
open QiniuFS
open Base
open FOP

[<TestFixture>]
type FOPTest() =

    [<Test>]
    member this.FopTest() =
        let w, h = 50, 50
        let fop = 
            Pipe [|
                ImageView2 { imageView2 with Mode = 1; W = w; H = h }
                Uri "imageInfo"
            |]
        let url = IO.publicUrlFop gogopherDomain gogopherKey fop
        let info = fopImageInfo url |> pickRetSynchro
        Assert.AreEqual(w, info.width)
        Assert.AreEqual(h, info.height)
