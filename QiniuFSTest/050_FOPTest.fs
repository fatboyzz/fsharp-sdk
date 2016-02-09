namespace QiniuFSTest

open System
open System.IO
open NUnit.Framework
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client
open TestBase
open FOP

[<TestFixture>]
type FOPTest() =

    [<Test>]
    member this.FopTest() =
        async {
            let w, h = 50, 50
            let fop = 
                Pipe [|
                    ImageView2 { imageView2 with Mode = 1; W = w; H = h }
                    ImageInfo
                |]
            let url = IO.publicUrlFop gogopherDomain gogopherKey fop
            let! ret = fopImageInfo url
            match ret with
            | Succ info -> 
                Assert.AreEqual(w, info.width)
                Assert.AreEqual(h, info.height)
            | Error e -> failwith e.error
        } |> Async.RunSynchronously
        
