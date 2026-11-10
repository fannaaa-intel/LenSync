Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>Create or edit a staff account (Settings > Users). New accounts get a temporary password.</summary>
Public Class UserDialog
    Inherits LsFormDialog

    Private Const FieldBlock As Integer = 92

    Private ReadOnly _existing As UserSummary
    Private ReadOnly _roles As List(Of Role)
    Private ReadOnly _txtName As New LsTextBox()
    Private ReadOnly _txtUser As New LsTextBox()
    Private ReadOnly _txtEmail As New LsTextBox()
    Private ReadOnly _role As New LsChoice()

    ''' <summary>Set after a successful create: show it once with TempPasswordDialog.</summary>
    Public Property TemporaryPassword As String
    Public Property CreatedUsername As String

    Public Sub New(roles As List(Of Role), Optional existing As UserSummary = Nothing)
        MyBase.New(If(existing Is Nothing, "New user", "Edit user"),
                   If(existing Is Nothing, "Add a staff account", existing.FullName),
                   If(existing Is Nothing, "They'll get a temporary password to replace the first time they log in.",
                                           "Username: " & existing.Username),
                   If(existing Is Nothing, "Create user", "Save changes"), "Cancel", 380, FieldBlock * 3 + 20 + 4 + 42 + 24)
        _existing = existing
        _roles = roles

        AddField("Full name", _txtName, 0)
        AddField("Username", _txtUser, FieldBlock)
        AddField("Email (needed for Forgot password)", _txtEmail, FieldBlock * 2)
        _txtName.MaxLength = 120
        _txtUser.MaxLength = 30
        _txtUser.Icon = LineIcon.User
        _txtEmail.MaxLength = 150
        _txtName.Placeholder = "e.g. Ana Cruz"
        _txtUser.Placeholder = "e.g. ana.cruz"
        _txtEmail.Placeholder = "name@gmail.com"

        Dim roleLabel = MakeLabel("Role", FieldBlock * 3)
        Body.Controls.Add(roleLabel)
        _role.SetItems(roles.Select(Function(r) r.Name))
        _role.Bounds = New Rectangle(0, roleLabel.Bottom + Theme.Dpi(4), Body.Width, Theme.Dpi(42))
        _role.AccessibleName = "Role"
        Body.Controls.Add(_role)

        If existing IsNot Nothing Then
            _txtName.Text = existing.FullName
            _txtUser.Text = existing.Username
            _txtUser.Enabled = False      ' usernames are permanent (they appear in the audit trail)
            _txtEmail.Text = existing.Email
            _role.SelectedIndex = roles.FindIndex(Function(r) r.Id = existing.RoleId)
            If existing.Id = Session.CurrentUserId Then _role.Enabled = False
        Else
            Dim cashier = roles.FindIndex(Function(r) String.Equals(r.Name, "Cashier", StringComparison.OrdinalIgnoreCase))
            _role.SelectedIndex = If(cashier >= 0, cashier, 0)
        End If
    End Sub

    Private Function MakeLabel(caption As String, y96 As Integer) As Label
        Return New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .Bounds = New Rectangle(0, Theme.Dpi(y96), Body.Width, Theme.LabelBold.Height + Theme.Dpi(4))}
    End Function

    Private Sub AddField(caption As String, box As LsTextBox, y96 As Integer)
        Dim lbl = MakeLabel(caption, y96)
        box.FieldName = caption
        box.Bounds = New Rectangle(0, lbl.Bottom + Theme.Dpi(4), Body.Width, Theme.Dpi(42))
        Body.Controls.Add(lbl)
        Body.Controls.Add(box)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _txtName.Focus()
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim name = _txtName.Text
        Dim username = _txtUser.Text
        Dim email = _txtEmail.Text
        Dim p = UserAdmin.FullNameProblem(name)
        If p IsNot Nothing Then Validator.SetError(_txtName, p)
        If _existing Is Nothing Then
            p = UserAdmin.UsernameProblem(username)
            If p IsNot Nothing Then Validator.SetError(_txtUser, p)
        End If
        p = UserAdmin.EmailProblem(email)
        If p IsNot Nothing Then Validator.SetError(_txtEmail, p)
        If _role.SelectedIndex < 0 Then Validator.SetError(_role, "Choose a role.")
        If Validator.HasErrors(Body) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return False
        End If

        Dim roleId = _roles(_role.SelectedIndex).Id
        SetBusy(True, "Saving...")
        Dim result = Await Task.Run(
            Function()
                If _existing Is Nothing Then Return UserAdmin.CreateUser(name, username, email, roleId)
                Return UserAdmin.UpdateUser(_existing.Id, name, email, roleId)
            End Function)
        SetBusy(False)
        Dim fields As New Dictionary(Of String, Control) From {
            {"fullName", _txtName}, {"username", _txtUser}, {"email", _txtEmail}, {"role", _role}}
        If FormErrors.Show(result, fields) Then
            Shake()
            Return False
        End If
        If _existing Is Nothing Then
            TemporaryPassword = CStr(result.Value)
            CreatedUsername = username.Trim()
        End If
        Return True
    End Function

End Class
