Public Class PageOtherTest

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Dim DownloadUri As Uri = Nothing
        If Not Uri.TryCreate(Url, UriKind.Absolute, DownloadUri) OrElse
           (DownloadUri.Scheme <> Uri.UriSchemeHttp AndAlso DownloadUri.Scheme <> Uri.UriSchemeHttps) Then
            Hint("下载地址无效，请输入以 http:// 或 https:// 开头的地址。", HintType.Red)
            Return
        End If

        Dim SafeFileName As String = GetSafeFileName(Url, FileName)
        Dim Target As String = Nothing
        If String.IsNullOrWhiteSpace(Folder) Then
            RunInUiWait(Sub() Target = Dialogs.SaveFile("选择保存位置", SafeFileName))
        Else
            DirectoryUtils.Create(Folder)
            Target = Path.Combine(Folder, SafeFileName)
        End If
        If String.IsNullOrWhiteSpace(Target) Then Return

        If FileUtils.Exists(Target) AndAlso
           MyMsgBox($"文件已存在：{Target}{vbCrLf}是否覆盖？", "确认覆盖", "覆盖", "取消", IsWarn:=True) = 2 Then Return

        Dim DownloadFile As New NetFile({Url}, Target)
        Dim Loader As New LoaderCombo(Of Integer)("自定义文件下载：" & PathUtils.GetLastPart(Target),
            {New LoaderDownload("下载自定义文件", New List(Of NetFile) From {DownloadFile}) With {.ProgressWeight = 1}}) With {
                .OnStateChanged =
                Sub(CurrentLoader)
                    Select Case CurrentLoader.State
                        Case LoadState.Finished
                            Hint("自定义文件下载完成！", HintType.Green)
                        Case LoadState.Failed
                            Hint("自定义文件下载失败：" & CurrentLoader.Error.GetDisplay(False), HintType.Red)
                        Case LoadState.Canceled
                            Hint("自定义文件下载已取消。", HintType.Blue)
                    End Select
                End Sub}
        Loader.Start(1)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
    End Sub

    Private Shared Function GetSafeFileName(Url As String, FileName As String) As String
        Dim Result As String = If(String.IsNullOrWhiteSpace(FileName), PathUtils.GetLastPart(Url.BeforeFirst("?").BeforeFirst("#")), FileName)
        Result = PathUtils.GetLastPart(Result).Trim.TrimEnd("."c)
        For Each InvalidChar As Char In Path.GetInvalidFileNameChars()
            Result = Result.Replace(InvalidChar, "_"c)
        Next
        If String.IsNullOrWhiteSpace(Result) Then Result = "download.bin"
        Return Result
    End Function

    Private Sub BtnCustomDownload_Click(sender As Object, e As EventArgs) Handles BtnCustomDownload.Click
        StartCustomDownload(TextCustomUrl.Text.Trim, TextCustomFileName.Text.Trim)
    End Sub

    Public Shared Sub Jrrp()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub RubbishClear()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        If ShowHint Then Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        If ShowHint Then Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Function GetRandomCave() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomPresetHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function

End Class
