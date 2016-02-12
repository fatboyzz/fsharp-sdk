Qiniu F# SDK 使用指南
===

## 通用部分

- 所有例子均打开了以下空间和模块

```f#
open QiniuFS
```

- 大部分 api 是异步调用。使用了 F# 异步流。

- 大部分 api 第一个参数是 Client 。用以下代码创建一个 Client。
注意以后的 api 如果需要均使用 c 作为第一个参数。

```f#
let c = client {
    config with
        accessKey = "<Please apply your access key>"
        secretKey = "<Dont send your secret key to anyone>"
}
```

- 大部分 api 异步操作完成之后的返回类型是 Ret<'a> 定义如下：

```f#
type Error = {
    error : String
}

type Ret<'a> =
| Succ of 'a
| Error of Error
```

用模式匹配取出返回值并分别处理，或者使用下面这些通用函数：

```f#
let mapRet (f : 'a -> 'b) (ret : Ret<'a>) =
    match ret with
    | Succ data -> Succ (f data)
    | Error e -> Error e

let pickRet (ret : Ret<'a>) =
    match ret with
    | Succ data -> data
    | Error e -> failwith e.error

let ignoreRet (ret : Ret<'a>) = 
    ret |> pickRet |> ignore

let checkRet (ret : Ret<'a>) =
    match ret with
    | Succ _ -> true
    | Error _ -> false
```

- 使用 F# 模块划分了功能。具体功能的标题中会有说明。

## 上传策略 (模块 IO)

- 创建 IO.PutPolicy

```f#
let policy = { 
    IO.putPolicy with
        scope = IO.scope <| entry tc.BUCKET key
        deadline = IO.defaultDeadline()
}
```

- 制作 uptoken

```f#
let uptoken = IO.sign c policy
```

## 小文件上传 (4M 以下，模块 IO)

- 服务器端生成一个 uptoken 。参见上传策略。
- 服务器通过网络把 uptoken 传给 客户端
- 客户端上传

```f#
IO.put c uptoken key stream IO.putExtra 
|> Async.RunSynchronously |> pickRet
```

## 大文件上传 (4M 以上，分块，并行，模块 IO、RIO)

- 服务器端生成一个 uptoken 。参见上传策略。
- 服务器通过网络把 uptoken 传给 客户端
- 客户端上传

```f#
RIO.rput c uptoken key stream RIO.rputExtra
|> Async.RunSynchronously |> pickRet
```

## 大文件上传，记录断点信息 (4M 以上，分块，并行，模块 IO、RIO)

- 需要记录的是上传过程中产生的 Progress ，定义如下

```f#
type ChunkSucc = {
    ctx : String
    checksum : String
    crc32 : UInt32
    offset : Int32
    host : String
}

type Progress = {
    blockId : Int32
    blockSize : Int32
    ret : ChunkSucc
}
```

 * 上传过程中文件先被切割成 block (默认 4M)，多个 block 并行上传。
 * block 内部， 一次 http 连接最多上传一个 chunk (默认 1M)，同一 block 的多个 chunk 串行上传。
 * chunk 上传成功了才会生成 Progress 。

- 上传时需要设置 RIO.RputExtra 中的 progresses 和 notify

```f#
type RPutExtra = {
    customs : (String * String)[]
    mimeType : String
    blockSize : Int32
    chunkSize : Int32
    bufSize : Int32
    tryTimes : Int32
    worker : Int32
    progresses : Progress[]
    notify : Progress -> unit
}
```

 * progresses 是上次上传时记录下的所有 Progress，没有记录可以是空数组。
 * notify 是在 chunk 下载成功后的回调函数，这里应该尽快序列化 Progress 。
 * notify 回调之前已经加了锁，保证同一时间只有一个线程回调 notify 。

- 下面代码使用 Json 序列化 Progress

```f#
use progressOutput = File.OpenWrite(progressesPath)
let notify (p : RIO.Progress) =
    writeJson progressStream p
```

- 下面代码读取用 Json 序列化的 Progresses

```f#
use progressInput = File.OpenRead(progressesPath)
let ps = readJsons<RIO.Progress> progressInput |> Seq.toArray 
```

## 获取下载 url (模块 IO)

- 公有 url，客户端可以直接生成

```f#
let url = IO.publicUrl domain key
```

- 私有 url，服务器生成之后传给客户端

```f#
let url = IO.privateUrl c domain key deadline
```

## 小文件下载 (4M以下，模块 IO、D)

- 获取 url。参见获取下载 url
- 客户端下载

```f#
D.down url D.downExtra path
|> Async.RunSynchronously |> pickRet
```

## 大文件下载 (分块，并行，模块 IO、RD)

- 获取 url。参见获取下载 url
- 客户端下载

```f#
RD.rdown url RD.rdownExtra path
|> Async.RunSynchronously |> pickRet	
```

## 大文件下载，记录断点信息 (4M 以上，分块，并行，模块 IO、RIO)
- 与上传同理，需要把 progresses 和 notify 填进 RD.RDownExtra，参见 大文件上传，记录断点信息

## 资源管理 (模块 RS、RSF)
- 指定文件的位置，后面代码中的 en，src，dst 都是文件位置。

```f#
let en = entry bucket key
```

- 查看、删除、复制、移动。

```f#
RS.stat c en
RS.delete c en
RS.copy c src dst
RS.move c src dst
```

- 批量操作

```f#
RS.batch c [| 
	OpStat(en)
	OpDelete(en)
	OpCopy(src, dst)
	OpMove(src, dst)
|]
```

- 抓取

```f#
RS.fetch c url dst
```

- 修改 Mime

```f#
RS.changeMime c mime en
```

- 列出文件

```f#
RS.list c bucket limit prefix delimiter marker
```

## 定义数据处理 (模块 FOP)

```f#
open QiniuFS.FOP

let fop = 
    Pipe [|
        ImageView2 { imageView2 with Mode = 1; W = w; H = h }
        ImageInfo
    |]
```

## 获取执行了指定数据处理的 url (模块 IO, FOP)

```f#
let url = IO.publicUrlFop domain key fop
let url = IO.privateUrlFop c domain key fop deadline
```

## 处理结果持久化 (模块 PFOP)

```f#
PFOP.pfop c en fop pfopExtra
```

## 查询处理结果 (模块 PFOP)

```f#
PFOP.prefop c persistentId
```
