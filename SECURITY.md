# Security policy

## Supported versions

Security fixes are applied to the latest version on the default branch. Older builds may not receive updates.

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability. Use GitHub's **Security** tab and choose **Report a vulnerability** to submit a private report to the repository owner.

Include the affected version or commit, impact, reproduction steps, and any suggested mitigation. Avoid including unrelated personal data, network captures, credentials, or secrets. You can expect an initial acknowledgement within seven days; remediation timing depends on severity and complexity.

## Security boundaries

Internet Meter does not install a packet-capture driver or inspect packet contents. The optional per-app traffic view starts an elevated helper because access to the required Windows kernel network events is restricted. The helper communicates locally and exits with the main application.

The Windows 10 deskband is an in-process Explorer shell extension. Installation therefore requires administrator approval and production packages should be Authenticode-signed before distribution.
