Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Settings

    ''' <summary>Settings > Roles &amp; access: which modules each role may open (Admin is always full access).</summary>
    Public Class AccessPage
        Inherits ModulePage

        Private ReadOnly _matrix As New PermissionMatrix()
        Private ReadOnly _notice As New InlineNotice()
        Private ReadOnly _btnSave As New LsButton("Save changes", ButtonVariant.Primary)
        Private ReadOnly _btnDiscard As New LsButton("Discard", ButtonVariant.Secondary)
        Private _saved As New HashSet(Of Tuple(Of Integer, Integer))
        Private _roles As New List(Of Role)
        Private _perms As New List(Of Permission)

        Public Sub New()
            MyBase.New("Roles & access", "Tick the modules each role may open. The sidebar hides everything else.")
            AddAction(_btnSave)
            AddAction(_btnDiscard)
            Controls.AddRange({_matrix, _notice})
            _notice.ShowMessage("Changes apply the next time each person logs in. The Admin role always keeps full access so Settings can't be locked.", NoticeKind.Info, False)
            AddHandler _matrix.Changed, Sub() UpdateButtons()
            AddHandler _btnSave.Click, Sub() SaveAsync()
            AddHandler _btnDiscard.Click, Sub() DiscardChanges()
            UpdateButtons()
        End Sub

        Public Overrides Async Function ReloadAsync() As Task
            Dim data = Await RunAsync(Function() Tuple.Create(UserAdmin.ListRoles(), UserAdmin.ListPermissions(), UserAdmin.ListGrants()),
                                      "Loading permissions")
            If data Is Nothing Then Return
            _roles = data.Item1
            _perms = data.Item2
            _saved = data.Item3
            _matrix.Bind(_roles, _perms, _saved)
            UpdateButtons()
            PerformLayout()
        End Function

        Public Overrides ReadOnly Property HasUnsavedChanges As Boolean
            Get
                Return _roles.Count > 0 AndAlso Not NonAdmin(_matrix.Grants).SetEquals(NonAdmin(_saved))
            End Get
        End Property

        ''' <summary>Only the editable (non-Admin) grants matter when comparing.</summary>
        Private Function NonAdmin(grants As HashSet(Of Tuple(Of Integer, Integer))) As HashSet(Of Tuple(Of Integer, Integer))
            Dim adminIds = _roles.Where(Function(r) r.IsAdmin).Select(Function(r) r.Id).ToList()
            Return New HashSet(Of Tuple(Of Integer, Integer))(grants.Where(Function(t) Not adminIds.Contains(t.Item1)))
        End Function

        Public Overrides Sub DiscardChanges()
            _matrix.Bind(_roles, _perms, _saved)
            UpdateButtons()
        End Sub

        Private Sub UpdateButtons()
            Dim dirty = HasUnsavedChanges
            _btnSave.Enabled = dirty
            _btnDiscard.Enabled = dirty
        End Sub

        Private Async Sub SaveAsync()
            Dim grants = _matrix.Grants
            Dim result = Await RunAsync(Function() UserAdmin.SaveGrants(grants), "Saving permissions")
            If result Is Nothing OrElse FormErrors.Show(result, Nothing) Then Return
            Await ReloadAsync()
            Toast.Show("Permissions saved. They apply at each person's next log-in.", ToastKind.Success)
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim w = Math.Min(area.Width, Theme.Dpi(900))
            Dim noticeH = Theme.Dpi(54)
            Dim gap = Theme.Dpi(14)
            ' Every row must stay visible: if space is short, the note gives way before the matrix does.
            Dim room = area.Height - noticeH - gap
            Dim showNotice = room >= _matrix.MinimumHeight
            Dim matrixH = If(showNotice, Math.Min(_matrix.PreferredHeight, room), Math.Min(_matrix.PreferredHeight, area.Height))
            _matrix.Bounds = New Rectangle(area.Left, area.Top, w, matrixH)
            _notice.Visible = showNotice
            _notice.Bounds = New Rectangle(area.Left, _matrix.Bottom + gap, w, noticeH)
        End Sub

    End Class

End Namespace
