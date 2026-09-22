# Package-consumer failure propagation

The installed-package consumer step explicitly selects `shell: bash`. GitHub
runs that shell with `-e -o pipefail`, so a failed `dotnet run` remains a failed
step even though stdout is also retained through `tee`. The unspecified Linux
shell uses `bash -e` without pipefail; a successful `tee` could otherwise hide
an SDK or consumer failure and allow packaging to continue.

The regression test extracts the actual workflow step body, requires its
explicit shell selection, and executes it under the documented Bash options.
A shell-function SDK probe covers success, consumer exit 23 and restore exit
17. It checks exact exit propagation, retained consumer output and no consumer
execution after a failed restore. Git for Windows Bash is selected explicitly
on Windows; no network, SDK or publishing credentials are used by the probes.
These probes test workflow control flow, not DXF behavior. The real installed-
package consumer and full conformance matrices still run in hosted CI.

The release flow reuses the same build workflow, so the correction applies to
ordinary builds and release rehearsals/tagged builds. No source test assertion,
package format, secret, permission or publication setting changes. Successful
consumer results from earlier runs remain valid; no prior failed consumer run
is asserted without its actual logs. This adds a missing failure barrier rather
than certifying every possible release failure or native AutoCAD compatibility.

Primary reference: [GitHub workflow shell behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idstepsshell).
