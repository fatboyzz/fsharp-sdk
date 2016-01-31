namespace QiniuTest

open System
open System.IO
open System.Net
open NUnit.Framework
open Qiniu
open Qiniu.Util
open Qiniu.Client
open TestBase

[<TestFixture>]
type RSTest() =
    [<Test>]
    member this.StatTest() =
        putString c ("stat.txt", "statstat") |> checkSynchro
        let e = entry tc.BUCKET "stat.txt"
        RS.stat c e |> checkSynchro
        RS.delete c e |> checkSynchro

    [<Test>]
    member this.DeleteTest() =
        putString c ("deleteMe.txt", "deleteMe") |> checkSynchro
        RS.delete c (entry tc.BUCKET "deleteMe.txt") |> checkSynchro

    [<Test>]
    member this.CopyTest() =
        let content = "orig"
        putString c ("copySrc.txt", content) |> checkSynchro
        let src = entry tc.BUCKET "copySrc.txt"
        let dst = entry tc.BUCKET "copyDst.txt"
        RS.copy c src dst |> checkSynchro
        let ret = getString c "copyDst.txt"
        Assert.AreEqual(content, ret)
        RS.delete c src |> checkSynchro
        RS.delete c dst |> checkSynchro

    [<Test>]
    member this.MoveTest() =
        putString c ("beforeMove.txt", "content") |> checkSynchro
        let src = entry tc.BUCKET "beforeMove.txt"
        let dst = entry tc.BUCKET "afterMove.txt"
        RS.move c src dst |> checkSynchro
        RS.delete c dst |> checkSynchro

    [<Test>]
    member this.BatchTest() =
        let entryOf (key : String) = entry tc.BUCKET key
        putString c ("a.txt", "a") |> checkSynchro
        putString c ("b.txt", "b") |> checkSynchro
        [|
            RS.OpMove(entryOf "a.txt", entryOf "temp.txt")
            RS.OpMove(entryOf "b.txt", entryOf "a.txt")
            RS.OpMove(entryOf "temp.txt", entryOf "b.txt")
        |]
        |> RS.batch c
        |> Async.RunSynchronously
        |> Seq.iter check

        Assert.AreEqual("b", getString c "a.txt")
        Assert.AreEqual("a", getString c "b.txt")
        RS.delete c <| entryOf "a.txt" |> checkSynchro
        RS.delete c <| entryOf "b.txt" |> checkSynchro

    [<Test>]
    member this.ChangeMimeTest() =
        putString c ("changeMime.txt", "changeMime") |> checkSynchro
        RS.changeMime c "text/html" (entry tc.BUCKET "changeMime.txt") |> checkSynchro
        RS.delete c <| (entry tc.BUCKET "changeMime.txt") |> checkSynchro
