Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Settings

    ''' <summary>Settings > Users: list, search, create, edit, reset password, deactivate/reactivate.</summary>
    Public Class UsersPage
        Inherits ModulePage

        Private ReadOnly _search As New LsTextBox()
        Private ReadOnly _grid As New LsGrid()
        Private ReadOnly _btnNew As New LsButton("New user", ButtonVariant.Primary)
        Private ReadOnly _btnEdit As New LsButton("Edit", ButtonVariant.Secondary)
        Private ReadOnly _btnReset As New LsButton("Reset password", ButtonVariant.Secondary)
        Private ReadOnly _btnActive As New LsButton("Deactivate", ButtonVariant.Secondary)
        Private _users As New List(Of UserSummary)
        Private _roles As New List(Of Role)
        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        Public Sub New()
            MyBase.New("Users", "Staff accounts and their roles. Accounts are deactivated, never deleted, so their history stays.")
            AddAction(_btnNew)
            _search.Placeholder = "Search name, username or email"
            _search.FieldName = "Search users"
            _search.Icon = LineIcon.User
            _grid.EmptyText = "No users match your search."
            ' Name is widest: it must fit "System Administrator  (you)" at a 1080p/150% content width.
            _grid.AddTextColumn("name", "Name", 25)
            _grid.AddTextColumn("username", "Username", 13)
            _grid.AddTextColumn("role", "Role", 12)
            _grid.AddTextColumn("email", "Email", 21)
            _grid.AddTextColumn("lastLogin", "Last log-in", 15)
            _grid.AddStatusColumn("status", "Status", 15)
            Controls.AddRange({_search, _btnEdit, _btnReset, _btnActive, _grid})

            AddHandler _search.TextChanged, Sub() Fill()
            AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
            AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()
            AddHandler _grid.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.Handled = True
                                              EditSelected()
                                          End If
                                      End Sub
            AddHandler _btnNew.Click, Sub() CreateUser()
            AddHandler _btnEdit.Click, Sub() EditSelected()
            AddHandler _btnReset.Click, Sub() ResetSelected()
            AddHandler _btnActive.Click, Sub() ToggleSelected()
            UpdateButtons()
        End Sub

        Public Overrides Async Function ReloadAsync() As Task
            Dim data = Await RunAsync(Function() Tuple.Create(UserAdmin.ListUsers(), UserAdmin.ListRoles()), "Loading users")
            If data Is Nothing Then Return
            _users = data.Item1
            _roles = data.Item2
            Fill()
        End Function

        Private Sub Fill(Optional selectId As Integer = 0)
            Dim keepId = If(selectId <> 0, selectId, If(TryCast(_grid.SelectedItem, UserSummary)?.Id, 0))
            Dim q = _search.Text.Trim()
            _grid.Rows.Clear()
            For Each u In _users
                If q.Length > 0 AndAlso
                   u.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                   u.Username.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                   u.Email.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                Dim lastLogin = "Never"
                If u.LastLogin.HasValue Then
                    Dim d = u.LastLogin.Value
                    ' Short and scannable: time for this year, the year for older log-ins.
                    lastLogin = d.ToString(If(d.Year = DateTime.Now.Year, "MMM d, h:mm tt", "MMM d, yyyy"), PH)
                End If
                Dim index = _grid.Rows.Add(u.FullName & If(u.Id = Session.CurrentUserId, "  (you)", ""), u.Username, u.RoleName,
                                           If(u.Email.Length > 0, u.Email, "—"), lastLogin, StatusOf(u))
                _grid.Rows(index).Tag = u
            Next
            If keepId <> 0 Then _grid.SelectWhere(Function(t) DirectCast(t, UserSummary).Id = keepId)
            UpdateButtons()
        End Sub

        Private Shared Function StatusOf(u As UserSummary) As String
            If Not u.IsActive Then Return "Inactive"
            If u.IsLocked Then Return "Locked"
            If u.MustChangePassword Then Return "Temp password"
            Return "Active"
        End Function

        Private ReadOnly Property Selected As UserSummary
            Get
                Return TryCast(_grid.SelectedItem, UserSummary)
            End Get
        End Property

        Private Sub UpdateButtons()
            Dim u = Selected
            Dim isSelf = u IsNot Nothing AndAlso u.Id = Session.CurrentUserId
            _btnEdit.Enabled = u IsNot Nothing
            _btnReset.Enabled = u IsNot Nothing AndAlso u.IsActive AndAlso Not isSelf
            _btnActive.Enabled = u IsNot Nothing AndAlso Not isSelf
            _btnActive.Text = If(u IsNot Nothing AndAlso Not u.IsActive, "Reactivate", "Deactivate")
            _btnActive.FitToText()
            PerformLayout()
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim gap = Theme.Dpi(10)
            Dim y = area.Top
            Dim right = area.Right
            For Each b In {_btnActive, _btnReset, _btnEdit}
                b.Location = New Point(right - b.Width, y + 1)
                right = b.Left - gap
            Next
            Dim searchW = Math.Max(Theme.Dpi(180), Math.Min(Theme.Dpi(360), right - area.Left - Theme.Dpi(16)))
            _search.Bounds = New Rectangle(area.Left, y, searchW, Theme.Dpi(40))
            Dim gridTop = _search.Bottom + Theme.Dpi(16)
            _grid.Bounds = New Rectangle(area.Left, gridTop, area.Width, Math.Max(0, area.Bottom - gridTop))
            ' On narrow windows drop the Email column so the rest stays readable (no squeezed cells).
            If _grid.Columns.Contains("email") Then _grid.Columns("email").Visible = area.Width >= Theme.Dpi(800)
        End Sub

        ' ---------------------------------------------------------------- actions
        Private Async Sub CreateUser()
            If _roles.Count = 0 Then Return
            Dim temp As String = Nothing
            Dim fullName As String = Nothing
            Dim username As String = Nothing
            Using dlg As New UserDialog(_roles)
                If dlg.ShowCard() <> DialogResult.OK Then Return
                temp = dlg.TemporaryPassword
                username = dlg.CreatedUsername
            End Using
            Await ReloadAsync()
            Dim created = _users.FirstOrDefault(Function(u) String.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            If created IsNot Nothing Then
                Fill(created.Id)
                fullName = created.FullName
            End If
            Using show As New TempPasswordDialog(If(fullName, username), username, temp, isNewAccount:=True)
                show.ShowCard()
            End Using
        End Sub

        Private Async Sub EditSelected()
            Dim u = Selected
            If u Is Nothing Then Return
            Using dlg As New UserDialog(_roles, u)
                If dlg.ShowCard() <> DialogResult.OK Then Return
            End Using
            Await ReloadAsync()
            Fill(u.Id)
            Toast.Show("User details saved.", ToastKind.Success)
            If u.Id = Session.CurrentUserId Then AppEvents.RaiseCurrentUserChanged()
        End Sub

        Private Async Sub ResetSelected()
            Dim u = Selected
            If u Is Nothing Then Return
            If Not LsDialog.Confirm($"{u.FullName} will get a new temporary password and must replace it at their next log-in. Their current password stops working now.",
                                    "Reset this password?", "Reset password", "Cancel") Then Return
            Dim id = u.Id
            Dim result = Await RunAsync(Function() UserAdmin.ResetPassword(id), "Resetting password")
            If result Is Nothing Then Return
            If FormErrors.Show(result, Nothing) Then Return
            Using show As New TempPasswordDialog(u.FullName, u.Username, CStr(result.Value), isNewAccount:=False)
                show.ShowCard()
            End Using
            Await ReloadAsync()
            Fill(id)
        End Sub

        Private Async Sub ToggleSelected()
            Dim u = Selected
            If u Is Nothing Then Return
            Dim activate = Not u.IsActive
            If Not activate AndAlso Not LsDialog.Confirm(
                $"{u.FullName} won't be able to log in. Their bookings, sales and history stay in the records.",
                "Deactivate this account?", "Deactivate", "Keep active") Then Return
            Dim id = u.Id
            Dim result = Await RunAsync(Function() UserAdmin.SetActive(id, activate), "Changing user status")
            If result Is Nothing OrElse FormErrors.Show(result, Nothing) Then Return
            Await ReloadAsync()
            Fill(id)
            Toast.Show(If(activate, $"{u.FullName} can log in again.", $"{u.FullName} was deactivated."), ToastKind.Success)
        End Sub

    End Class

End Namespace
