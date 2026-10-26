Public Class PageDownloadPlugin
    Implements IRefreshable

    Private PluginSearchId As Integer = 0
    Private SelectedServerFolder As String = Nothing

    Private Sub PageDownloadPlugin_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        InitPluginSources()
        RefreshServers()
    End Sub

    ''' <summary>初始化插件来源列表。Tag 为空表示 Modrinth 官方接口，否则为麦块开放 API 的来源段。</summary>
    Private Sub InitPluginSources()
        If ComboPluginSource.Items.Count > 0 Then Return
        ComboPluginSource.Items.Add(New MyComboBoxItem With {.Content = "Modrinth（官方接口）", .Tag = ""})
        ComboPluginSource.Items.Add(New MyComboBoxItem With {.Content = GetMinekuaiPluginSourceName("modrinth"), .Tag = "modrinth"})
        ComboPluginSource.Items.Add(New MyComboBoxItem With {.Content = GetMinekuaiPluginSourceName("spigot") & "（仅跳转网页）", .Tag = "spigot"})
        ComboPluginSource.Items.Add(New MyComboBoxItem With {.Content = GetMinekuaiPluginSourceName("curseforge"), .Tag = "curseforge"})
        ComboPluginSource.SelectedIndex = 0
    End Sub

    Private Function GetSelectedPluginSourceKey() As String
        Dim Item = TryCast(ComboPluginSource.SelectedItem, MyComboBoxItem)
        Return If(Item?.Tag?.ToString, "")
    End Function

    Private Function GetCurrentPluginSourceName() As String
        Dim Key As String = GetSelectedPluginSourceKey()
        Return If(Key = "", "Modrinth（官方接口）", GetMinekuaiPluginSourceName(Key))
    End Function

    Private Sub ComboPluginSource_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboPluginSource.SelectionChanged
        If SelectedServerFolder Is Nothing Then Return
        PanPluginResults.Children.Clear()
        HintPlugin.Theme = MyHint.Themes.Blue
        HintPlugin.Text = $"当前插件来源：{GetCurrentPluginSourceName()}。" &
                         If(GetSelectedPluginSourceKey() = "spigot", "Spigot 来源不提供 Minecraft 版本信息，且需要跳转网页手动下载。", "")
    End Sub

    Private Sub RefreshServers()
        Dim Previous As String = SelectedServerFolder
        ComboPluginServer.Items.Clear()
        Dim ServerFolders As New List(Of String)
        For Each Folder In GetServerFolders()
            Dim Config = LoadServerConfig(Folder)
            If Not SupportsServerPlugins(Folder, Config) Then Continue For
            ServerFolders.Add(Folder)
            Dim CoreName As String = GetConfiguredCoreName(Config)
            ComboPluginServer.Items.Add(New MyComboBoxItem With {
                .Content = $"{Config("Name")}（{CoreName} {Config("MinecraftVersion")}）",
                .Tag = Folder
            })
        Next
        If ComboPluginServer.Items.Count = 0 Then
            SelectedServerFolder = Nothing
            TextPluginGameVersion.Text = ""
            PanPluginResults.Children.Clear()
            SearchPlugin.IsEnabled = False
            TextPluginGameVersion.IsEnabled = False
            BtnSearchPlugins.IsEnabled = False
            ComboPluginSource.IsEnabled = False
            HintPlugin.Theme = MyHint.Themes.Yellow
            HintPlugin.Text = "没有可用于安装插件的 Paper、Purpur 或 MSLMC 混合服务器，请先在服务器核心中创建。"
            Return
        End If
        Dim Preferred = ComboPluginServer.Items.Cast(Of MyComboBoxItem)().
            FirstOrDefault(Function(item) Previous IsNot Nothing AndAlso PathUtils.IsSame(item.Tag.ToString, Previous))
        ComboPluginServer.SelectedItem = If(Preferred, ComboPluginServer.Items(0))
    End Sub

    Private Sub ComboPluginServer_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboPluginServer.SelectionChanged
        If ComboPluginServer.SelectedItem Is Nothing Then Return
        SelectedServerFolder = CType(ComboPluginServer.SelectedItem, MyComboBoxItem).Tag.ToString
        Dim Config = LoadServerConfig(SelectedServerFolder)
        TextPluginGameVersion.Text = Config("MinecraftVersion")?.ToString
        SearchPlugin.IsEnabled = True
        TextPluginGameVersion.IsEnabled = True
        BtnSearchPlugins.IsEnabled = True
        ComboPluginSource.IsEnabled = True
        PanPluginResults.Children.Clear()
        HintPlugin.Theme = MyHint.Themes.Blue
        HintPlugin.Text = If(String.IsNullOrWhiteSpace(TextPluginGameVersion.Text),
                             "请先填写服务器的 Minecraft 版本。", $"目标服务器已就绪，可以搜索 {GetCurrentPluginSourceName()} 插件。")
    End Sub

    Private Sub BtnRefreshPluginServers_Click(sender As Object, e As EventArgs) Handles BtnRefreshPluginServers.Click
        RefreshServers()
    End Sub

    Private Sub RefreshSelf() Implements IRefreshable.Refresh
        RefreshServers()
    End Sub

    Private Sub BtnSearchPlugins_Click(sender As Object, e As EventArgs) Handles BtnSearchPlugins.Click
        If SelectedServerFolder Is Nothing Then Return
        Dim MinecraftVersion As String = TextPluginGameVersion.Text.Trim
        If String.IsNullOrWhiteSpace(MinecraftVersion) Then
            Hint("请先填写兼容的 Minecraft 版本。", HintType.Red)
            Return
        End If
        PluginSearchId += 1
        Dim CurrentSearchId As Integer = PluginSearchId
        Dim TargetFolder As String = SelectedServerFolder
        Dim SearchText As String = SearchPlugin.Text.Trim
        Dim SourceKey As String = GetSelectedPluginSourceKey()
        Dim SourceName As String = GetCurrentPluginSourceName()
        PanPluginResults.Children.Clear()
        BtnSearchPlugins.IsEnabled = False
        HintPlugin.Theme = MyHint.Themes.Blue
        HintPlugin.Text = $"正在从 {SourceName} 搜索插件……"
        RunInNewThread(
        Sub()
            Try
                Dim Results As List(Of ServerPluginSearchResult)
                If SourceKey = "" Then
                    Results = SearchModrinthServerPlugins(SearchText, MinecraftVersion)
                Else
                    Results = SearchMinekuaiServerPlugins(SearchText, MinecraftVersion, SourceKey)
                    '按目标服务端核心过滤掉明显不兼容的加载器
                    Dim ServerConfig = LoadServerConfig(TargetFolder)
                    Dim AcceptedLoaders = GetAcceptedPluginLoaders(InferServerCoreType(TargetFolder, ServerConfig), TargetFolder, ServerConfig)
                    If AcceptedLoaders.Any() Then
                        Results = Results.Where(Function(item) Not item.Loaders.Any() OrElse
                                                               item.Loaders.Any(Function(loader) AcceptedLoaders.Contains(loader))).ToList
                    End If
                End If
                RunInUi(
                Sub()
                    If CurrentSearchId <> PluginSearchId OrElse Not PathUtils.IsSame(TargetFolder, SelectedServerFolder) Then Return
                    BtnSearchPlugins.IsEnabled = True
                    If Results.Count = 0 Then
                        HintPlugin.Theme = MyHint.Themes.Yellow
                        HintPlugin.Text = $"没有找到兼容的插件（来源：{SourceName}）。"
                        Return
                    End If
                    HintPlugin.Theme = MyHint.Themes.Blue
                    HintPlugin.Text = $"从 {SourceName} 找到 {Results.Count} 个插件。点击结果下载最新兼容版本。"
                    For Each Result In Results
                        Dim CurrentResult = Result
                        Dim Item As New MyListItem With {
                            .Title = CurrentResult.Title,
                            .Info = $"{CurrentResult.Author} · {FormatServerPluginDownloads(CurrentResult.DownloadCount)} · {CurrentResult.Loaders.Join(" / ")} · {CurrentResult.Description}",
                            .Height = 52,
                            .MinPaddingRight = 35,
                            .LogoScale = 0.82,
                            .Logo = CurrentResult.IconUrl,
                            .Tag = CurrentResult
                        }
                        AddHandler Item.Click,
                            Sub(senderItem As Object, args As MouseButtonEventArgs)
                                DownloadPlugin(CurrentResult, TargetFolder, MinecraftVersion)
                            End Sub
                        PanPluginResults.Children.Add(Item)
                    Next
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    If CurrentSearchId <> PluginSearchId Then Return
                    BtnSearchPlugins.IsEnabled = True
                    HintPlugin.Theme = MyHint.Themes.Red
                    HintPlugin.Text = $"{SourceName} 插件搜索失败：" & ex.GetDisplay(False)
                End Sub)
            End Try
        End Sub, "Search Server Plugins")
    End Sub

    Private Sub DownloadPlugin(Project As ServerPluginSearchResult, Folder As String, MinecraftVersion As String)
        'Spigot 来源的下载链接需要登录网页，改为直接打开资源页面
        If String.Equals(Project.SourceKey, "spigot", StringComparison.OrdinalIgnoreCase) Then
            Hint("Spigot 来源需要在网页上手动下载，已为你打开资源页面。", HintType.Blue)
            If Not String.IsNullOrWhiteSpace(Project.ExternalUrl) Then OpenWebsite(Project.ExternalUrl)
            Return
        End If
        Dim Config = LoadServerConfig(Folder)
        If Not SupportsServerPlugins(Folder, Config) Then
            Hint("目标服务器不是 Paper 或 Purpur，无法安装 Bukkit 插件。", HintType.Red)
            Return
        End If
        Dim CoreType As ServerCoreType = InferServerCoreType(Folder, Config)
        RunInNewThread(
        Sub()
            Try
                Dim PluginFile As ServerPluginFile
                If String.IsNullOrWhiteSpace(Project.SourceKey) Then
                    PluginFile = GetLatestModrinthPluginFile(Project, MinecraftVersion, CoreType, Folder, Config)
                Else
                    PluginFile = GetLatestMinekuaiPluginFile(Project, MinecraftVersion, CoreType, Folder, Config)
                End If
                Dim PluginFolder As String = Path.Combine(Folder, "plugins")
                Dim Target As String = Path.Combine(PluginFolder, PluginFile.FileName)
                Dim Overwrite As Integer = RunInUiWait(
                    Function()
                        If FileUtils.Exists(Target) Then
                            Return MyMsgBox($"插件文件已存在：{vbCrLf}{Target}{vbCrLf}{vbCrLf}是否覆盖？",
                                            "确认覆盖插件", "覆盖", "取消", IsWarn:=True)
                        End If
                        Return 1
                    End Function)
                If Overwrite = 2 Then Return

                DirectoryUtils.Create(PluginFolder)
                Dim Sources = {PluginFile.Url, PluginFile.MirrorUrl}.
                    Where(Function(url) Not String.IsNullOrWhiteSpace(url)).Distinct.ToArray
                Dim Checker As New FileChecker With {.Hash = PluginFile.Hash, .ActualSize = PluginFile.Size}
                Dim DownloadFile As New NetFile(Sources, Target, Checker, SimulateBrowserHeaders:=True)
                Dim DownloadLoader As New LoaderDownload($"下载插件：{PluginFile.ProjectTitle}",
                    New List(Of NetFile) From {DownloadFile}) With {
                    .OnStateChanged =
                    Sub(CurrentLoader)
                        Select Case CurrentLoader.State
                            Case LoadState.Finished
                                RunInUi(Sub() Hint($"插件 {PluginFile.FileName} 下载完成，重启服务器后生效。", HintType.Green))
                            Case LoadState.Failed
                                RunInUi(Sub() Hint("插件下载失败：" & CurrentLoader.Error.GetDisplay(False), HintType.Red))
                            Case LoadState.Canceled
                                RunInUi(Sub() Hint("插件下载已取消。", HintType.Blue))
                        End Select
                    End Sub}
                Dim Loader As New LoaderCombo(Of Integer)(
                    $"下载插件：{PluginFile.ProjectTitle}",
                    New List(Of LoaderBase) From {DownloadLoader})
                RunInUi(
                Sub()
                    Loader.Start(1)
                    LoaderTaskbarAdd(Loader)
                    FrmMain.BtnExtraDownload.ShowRefresh()
                    Hint($"正在下载 {PluginFile.ProjectTitle} {PluginFile.VersionNumber}……", HintType.Blue)
                End Sub)
            Catch ex As Exception
                RunInUi(Sub() Hint("无法准备插件下载：" & ex.GetDisplay(False), HintType.Red))
            End Try
        End Sub, "Prepare Server Plugin Download")
    End Sub

End Class
