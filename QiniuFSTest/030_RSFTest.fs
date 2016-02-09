namespace QiniuFSTest

open System
open NUnit.Framework
open QiniuFS
open QiniuFS.Util
open QiniuFS.Client
open TestBase

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
            async {
                let! ret = RSF.list c tc.BUCKET listLimit "" "" marker
                match ret with
                | Succ succ ->
                    if nullOrEmpty succ.marker 
                    then return acc + succ.items.Length
                    else return! loop succ.marker (acc + succ.items.Length)
                | Error e -> 
                    return failwith e.error
            }
        let acc = loop "" 0 |> Async.RunSynchronously
        Assert.AreEqual(listLength, acc)