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
    let smallUrl = IO.publicUrl tc.DOMAIN smallKey
    let downSmallPath = Path.Combine(testPath, smallKey)

    let bigKey = String.Format("{0}_{1}", ticks(), bigName)
    let bigUrl = IO.publicUrl tc.DOMAIN bigKey
    let downBigPath = Path.Combine(testPath, bigKey)

    [<Test>]
    member this.DTest() =
        IO.putFile c (uptoken smallKey) smallKey smallPath IO.putExtra |> checkSynchro
        D.downFile smallUrl D.downExtra downSmallPath |> checkSynchro
        let downMD5 = md5File downSmallPath
        File.Delete downSmallPath
        Assert.AreEqual(smallMD5, downMD5)

    [<Test>]
    member this.RDTest() =
        IO.putFile c (uptoken bigKey) bigKey bigPath IO.putExtra |> checkSynchro
        RD.rdownFile bigUrl RD.rdownExtra downBigPath |> checkSynchro
        let downMD5 = md5File downBigPath
        File.Delete downBigPath
        Assert.AreEqual(bigMD5, downMD5)
