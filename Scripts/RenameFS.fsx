open System
open System.IO
open System.Diagnostics

let git (args : String[]) =
    let p = new Process()
    p.StartInfo.FileName <- "git.exe"
    p.StartInfo.Arguments <- String.Join(" ", args)
    p.StartInfo.UseShellExecute <- false
    p.StartInfo.RedirectStandardOutput <- true
    p.Start() |> ignore
    Console.Out.Write(p.StandardOutput.ReadToEnd())
    p.WaitForExit()

let digits = [| '0' .. '9' |]

let renameFS (path : String) =
    let fs = Directory.GetFiles(path)
             |> Array.filter (fun s -> s.EndsWith(".fs"))
             |> Array.map Path.GetFileName
             |> Array.sort
    let d = int32 (Math.Log10(float fs.Length)) + 1
    let format = String.Format("{{0:D{0}}}0{{1}}", d) 
    let ids = [| 0 .. fs.Length - 1 |]

    Array.zip ids fs
    |> Array.map (fun (id, src) -> 
        let dst = String.Format(format, id, src.Trim(digits))
        printfn "%s %s" src dst
        if src <> dst then
            git [| "mv"; Path.Combine(path, src); Path.Combine(path, dst) |])

renameFS "../QiniuFS"
