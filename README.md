# BuffInspector

在 CS2 游戏内解析 BUFF 分享链接并更换皮肤。

## 使用方法

```text
!buff <BUFF 分享链接>
```

## 配置

SwiftlyS2 自动生成配置文件：`swiftlys2/configs/plugins/BuffInspector/buff-inspector.jsonc`。

## 演示

![BuffInspector1](assets/BuffInspector1.png)
![BuffInspector2](assets/BuffInspector2.png)

## 依赖与构建

- .NET 10 SDK
- [SwiftlyS2 v1.5.0](https://github.com/swiftly-solution/swiftlys2/releases/tag/v1.5.0)
- [WeaponSkins v0.6.0-ci.24](https://github.com/samyycX/WeaponSkins/releases/tag/v0.6.0-ci.24)

```powershell
dotnet build BuffInspector.sln -c Release
```

将 `src/BuffInspector/bin/Release/net10.0/` 内文件复制到服务器的 `swiftlys2/plugins/BuffInspector/`。
