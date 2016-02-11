namespace QiniuFSTest

open System
open System.IO
open NUnit.Framework
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client
open Base

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
        IO.putFile c (uptoken smallKey) smallKey smallPath IO.putExtra |> ignoreRetSynchro
        D.downFile smallUrl D.downExtra downSmallPath |> ignoreRetSynchro
        let downQETag = QETag.hashFile downSmallPath
        if File.Exists downSmallPath then File.Delete downSmallPath
        RS.delete c smallEntry |> ignoreRetSynchro
        Assert.AreEqual(smallQETag, downQETag)

    [<Test>]
    member this.RDTest() =
        IO.putFile c (uptoken bigKey) bigKey bigPath IO.putExtra |> ignoreRetSynchro
        RD.rdownFile bigUrl RD.rdownExtra downBigPath |> ignoreRetSynchro
        let downQETag = QETag.hashFile downBigPath
        if File.Exists downBigPath then File.Delete downBigPath
        RS.delete c bigEntry |> ignoreRetSynchro
        Assert.AreEqual(bigQETag, downQETag)
        