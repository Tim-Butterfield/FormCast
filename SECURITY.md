# Security Policy

## Reporting a vulnerability

Please report suspected security issues privately via GitHub's security
advisory feature:

<https://github.com/Tim-Butterfield/FormCast/security/advisories/new>

Do not open a public GitHub issue for a suspected vulnerability.
Responsible disclosure lets a fix reach affected users before details
of the exploit do.

## Supported versions

FormCast is a solo-maintained project. Security fixes are applied to
the most recent release. Please upgrade to the latest published version
before reporting.

| Version | Supported |
|---|---|
| Latest release | Yes |
| Prior releases | No |

## Scope

In scope:

* The FormCast plugin DLL (`FormCast.dll`) and its `ITCCPlugin`
  dispatch surface
* The `FormCast.Host.exe` cross-process daemon and its named-pipe
  protocol
* JSONC template parsing and `${var}` substitution
* `@FORMBIND` declarative event binding and `@FORMAPPLYBINDINGS`
  activation

Out of scope (report to the upstream maintainer):

* Vulnerabilities in JP Software TCC itself or `TC-DotNetPluginHost64.dll`
  (report to JP Software)
* Vulnerabilities in the .NET Framework, Windows, or WPF/WinForms
  (report to Microsoft)
* Bugs in user-authored BTM scripts, JSONC templates, or custom
  worker code that consume FormCast
