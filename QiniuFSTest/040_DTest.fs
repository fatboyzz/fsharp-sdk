namespace QiniuFSTest

open System
open System.IO
open NUnit.Framework
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client
open TestBase

[<TestFixture>]
type DTest() =
    let smallKey = String.Format("{0}_{1}", ticks(), smallName)
    let smallEntry = entry tc.BUCKET smallKey
    let smallUrl = IO.publicUrl tc.DOMAIN smallKey
    let downSmallPath = Path.Combine(testPath, smallKey)

    let bigKey = String.Format("{0}_{1}", ticks(), bigName)
    let bigEntry = entry tc.BUCKET bigKey
    let bigUrl = IO.publicUrl tc.DOMAIN bigKey
    let downBigPath = Path.Combine(testPath, bigKey)

    [<Test>]
    member this.DTest() =
        async {
            do! IO.putFile c (uptoken smallKey) smallKey smallPath IO.putExtra |>> check
            do! D.downFile smallUrl D.downExtra downSmallPath |>> check
            let downQETag = QETag.hashFile downSmallPath
            if File.Exists downSmallPath then File.Delete downSmallPath
            do! RS.delete c smallEntry |>> check
            Assert.AreEqual(smallQETag, downQETag)
        } |> Async.RunSynchronously

    [<Test>]
    member this.RDTest() =
        async {
            do! RIO.rputFile c (uptoken bigKey) bigKey bigPath RIO.rputExtra |>> check
            do! RD.rdownFile bigUrl RD.rdownExtra downBigPath |>> check
            let downQETag = QETag.hashFile downBigPath
            if File.Exists downBigPath then File.Delete downBigPath
            do! RS.delete c bigEntry |>> check
            Assert.AreEqual(bigQETag, downQETag)
        } |> Async.RunSynchronously
        