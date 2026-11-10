Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Models
Imports LenSync.Modules.Settings

Namespace Modules

    ''' <summary>Settings module (Admin): tabs for Users, Roles &amp; access, Packages, Add-ons, Studio and Email.</summary>
    Public Class SettingsView
        Inherits TabbedView

        Public Sub New()
            MyBase.New({"Users", "Roles & access", "Packages", "Add-ons", "Studio", "Email"})
        End Sub

        Protected Overrides Function CreatePage(index As Integer) As ModulePage
            Select Case index
                Case 0 : Return New UsersPage()
                Case 1 : Return New AccessPage()
                Case 2 : Return New PricingPage(PriceItemKind.Package)
                Case 3 : Return New PricingPage(PriceItemKind.Addon)
                Case 4 : Return New StudioPage()
                Case Else : Return New EmailPage()
            End Select
        End Function

    End Class

End Namespace
