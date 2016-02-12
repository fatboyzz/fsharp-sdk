module QiniuFS.RSF

open System
open RS

type ListItem = {
    key : String
    hash : String
    fsize : Int64
    putTime : Int64
    mimeType : String
    endUser : String
}

type ListSucc = {
    marker : String
    commonPrefixes : String[]
    items : ListItem[]
}

let list (c : Client) (bucket : String) (limit : Int32) (prefix : String) (delimiter : String) (marker : String) =
    let query = 
        seq { 
            yield String.Format("bucket={0}", bucket)
            if limit > 0 then yield String.Format("limit={0}", limit)
            if nullOrEmpty prefix |> not then yield String.Format("prefix={0}", prefix)
            if nullOrEmpty delimiter |> not then yield String.Format("delimiter={0}", delimiter)
            if nullOrEmpty marker |> not then yield String.Format("marker={0}", marker)
        } |> interpolate "&" |> concat
    let url = String.Format("{0}/list?{1}", c.config.rsfHost, query)
    url |> requestOp c |> responseJson<ListSucc>
   