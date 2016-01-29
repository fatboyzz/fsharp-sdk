Qiniu F# SDK 
===

## 特别注意

- 数据处理的 api 还没有完成。
- 项目还在测试阶段，可能做各种修改，没有放到 nuget 上，文档也需要进一步完善。

## 下载源码

	git clone https://github.com/fatboyzz/fsharp-sdk

## 编译
	
从 NuGet下载依赖的库 NewtonSoft.Json 和 NUnit 。
- 右键解决方案 -> 管理 NuGet 程序包 -> 还原。

编译
- 生成 -> 生成解决方案

## 测试

下载 Visual Studio 的 Nunit 扩展。
- 工具 -> 扩展和更新 -> NUnit3 Test Adapter。
- 测试 -> 窗口 -> 测试资源管理器

生成解决方案，之后测试资源管理器中多出测试项。
- 生成 -> 生成解决方案

设置环境变量
- 新建一个临时文件夹，比如 M:\qiniutest
- 设置环境变量 QINIU_TEST_PATH 为 M:\qiniutest

新建测试空间
- 登陆七牛管理页面，新建对象存储 Bucket "qiniutest"。

填写配置文件
- 复制项目下的 TestConfigTemplate.json 到 M:\QiniuTest 。并改名成 TestConfig.json
- 文本编辑器打开 TestConfig.json 设置 ACCESS_KEY，SECRET_KEY 和 "qiniutest" 的 DOMAIN。

运行测试，这会在 QINIU_TEST_PATH 中生成并上传下载一些文件，测试时间跟网络环境有关，大概 50 秒。
如果有测试不通过请发 issue。
- 测试资源管理器 -> 全部运行

## 使用

参见 USAGE.md

