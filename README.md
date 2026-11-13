# 更简单的过滤（EasierFiltering）

《深空当铺》(Probably Stolen) 模组。对任意装水容器一次性过滤：自动使用仓库中的过滤器（优先高级），并在执行前预览耐久消耗与结果纯净度。

A quality-of-life mod for *Probably Stolen*. Filter any filled water container in one action, using spare filters from storage (advanced first), with a durability and purity preview before you confirm.

## 功能

- 新增物品「便携过滤装置」：对准已装水的容器打开预览窗口
- 预览范围内的过滤器库存、目标水质能否达到、消耗的耐久、结果纯净度与水量
- 确认后原地过滤，不必用水壶反复倾倒
- 可选：拥有紫外线灯时去除微生物（不消耗耐久）
- 耗尽的滤芯可留在原位、丢进垃圾桶，或移到主仓库 / 后台柜台
- 默认读档时若还没有该工具，会放到后台柜台（放不下则改放主仓库）

设置在游戏「模组」菜单的齿轮里：搜索范围、耗尽滤芯去向、是否用紫外线灯、是否按真实耐久计算、是否使用非常规滤材（如 Kotton 布）。

## 安装

把编译产物放到游戏的 `Mods/EasierFiltering/`（需包含 `manifest.xml`、`EasierFiltering.dll` 以及 `Textures/`、`Localization/`）。启用后重启游戏。

本仓库是源码，作为作者私有工作区的 Git 子模块编译：项目引用工作区共享的 `build/PsmMod.props`。单独克隆后无法直接 `dotnet build`，请把本目录放到该工作区根目录后再编译，或把已编译的 DLL 与资源复制进游戏 `Mods/`。

## 清单

| 字段 | 值 |
|---|---|
| ID | `easier_filtering` |
| 游戏版本 | `1.0.0` |
| 能力 | 无 |
