# 基于体素化的 NavMesh 生成工具

> 1. 这是一个本科毕业设计作品，主要目的是学习 NavMesh 生成流程及其相关算法。
> 2. 项目使用了 Odin 插件辅助开发，所以需要将 Odin 插件导入到项目中，才可以正常运行

## 使用指南

用 Unity 打开项目，点击菜单栏的 `Navigation` -> `Generator` 打开生成工具面板, 如下图所示：

![Generator 面板](https://cdn.jsdelivr.net/gh/FcAYH/Images/2024/05/27/2050be28bd5c162d7fb841789fcbd140.png)

可以在这个面板中设置 NavMesh 作用的 Agent，并设置相关参数，如高度、半径、爬坡能力，爬台阶能力

在 Areas 页签中，可以设置 NavMesh 的区域，并为不同的区域设置不同的寻路权重，不过这个功能*暂未开发完成*

如果想要让一个物体被 NavMesh 生成器关注，需要在这个物体上挂载 `StaticObject` 组件，如下图所示：
![StaticObject](https://cdn.jsdelivr.net/gh/FcAYH/Images/2024/05/28/8e1782151efb91514ab37305b7025445.png)

在 Build 页签中，可以设置 NavMesh 的生成参数。

![Build 面板](https://cdn.jsdelivr.net/gh/FcAYH/Images/2024/05/28/c951eb2894b0f3c50a156b0ec77acc36.png)

点击 `Build` 按钮，即可为当前场景生成 NavMesh

## 运行效果

![运行效果](https://cdn.jsdelivr.net/gh/FcAYH/Images/2024/05/28/197e3c9751e3a711e8f82bcef73f08cf.png)

## 不足

1. 生成效率较低；
2. 目前虽然会将场景切分成多个 Tile，但是因为时间原因，没有实现多线程生成以及最终 NavMesh 的合并；
3. Editor 工具仍存在很多 bug 和操作不便的地方；
4. Area 功能未开发完成；
5. 寻路部分仅实现了基于边中点的 A* 寻路，未实现拉绳法平滑路径；
6. 未实现动态避障功能；

在后续我会抽空尽量去将这个项目完善。

> 希望该项目可以帮到正在学习导航网格生成的你，如果有任何问题，欢迎提 issue，我会尽快回复。
