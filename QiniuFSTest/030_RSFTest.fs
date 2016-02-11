namespace QiniuFSTest

open System
open NUnit.Framework
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client
open Base

[<TestFixture>]
type RSFTest() =
    
    let listLength = 20
    let listLimit = 5

    [<SetUp>]
    member this.SetUp() =
        [| 0 .. listLength - 1 |]
        |> Array.map (fun id -> 
            let s = id.ToString()
            putString c (s, s))
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.map ignoreRet
        |> ignore

    [<TearDown>]
    member this.TearDown() =
        [| 0 .. listLength - 1 |]
        |> Array.map (fun id -> 
            let e = entry tc.BUCKET (id.ToString())
            RS.delete c e)
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.map ignoreRet
        |> ignore

    [<Test>]
    member this.ListTest() =
        let rec loop marker acc =
            let ret = RSF.list c tc.BUCKET listLimit "" "" marker |> pickRetSynchro
            if nullOrEmpty ret.marker 
            then acc + ret.items.Length
            else loop ret.marker (acc + ret.items.Length) 
        Assert.AreEqual(listLength, loop "" 0)