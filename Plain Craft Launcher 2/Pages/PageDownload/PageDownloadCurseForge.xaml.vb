Public Class PageDownloadCurseForge

    Private Sub PageDownloadCurseForge_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        TextCurseForgeKey.Password = CurseForgeAPIKey
        RefreshKeyState()
    End Sub

    Private Sub RefreshKeyState()
        If String.IsNullOrWhiteSpace(CurseForgeAPIKey) Then
            HintKeyState.Theme = MyHint.Themes.Yellow
            HintKeyState.Text = "尚未配置 CurseForge API Key。未配置时只能使用 Modrinth 资源。"
        Else
            HintKeyState.Theme = MyHint.Themes.Blue
            HintKeyState.Text = $"已配置 CurseForge API Key（{CurseForgeAPIKey.Length} 个字符），内容已隐藏。"
        End If
    End Sub

    Private Sub BtnSaveKey_Click(sender As Object, e As EventArgs) Handles BtnSaveKey.Click
        Dim NewKey As String = TextCurseForgeKey.Password.Trim
        Settings.Set("ToolCurseForgeAPIKey", NewKey)
        SetCurseForgeAPIKey(NewKey)
        If NewKey = "" Then
            RefreshKeyState()
            Return
        End If
        BtnSaveKey.IsEnabled = False
        HintKeyState.Theme = MyHint.Themes.Blue
        HintKeyState.Text = "正在验证 CurseForge API Key……"
        RunInNewThread(
        Sub()
            Try
                NetRequestByClientRetry($"https://api.curseforge.com/v1/games?t={GetTimeMs()}",
                                        Accept:="application/json", RequireJson:=True, SimulateBrowserHeaders:=False)
                RunInUi(
                Sub()
                    HintKeyState.Theme = MyHint.Themes.Blue
                    HintKeyState.Text = "CurseForge API Key 有效，已保存。"
                    BtnSaveKey.IsEnabled = True
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    HintKeyState.Theme = MyHint.Themes.Red
                    HintKeyState.Text = $"CurseForge API Key 验证失败：{ex.Message}"
                    BtnSaveKey.IsEnabled = True
                End Sub)
            End Try
        End Sub, "CurseForge Key Check")
    End Sub

    Private Sub BtnClearKey_Click(sender As Object, e As EventArgs) Handles BtnClearKey.Click
        TextCurseForgeKey.Password = ""
        Settings.Set("ToolCurseForgeAPIKey", "")
        SetCurseForgeAPIKey("")
        RefreshKeyState()
    End Sub

End Class
