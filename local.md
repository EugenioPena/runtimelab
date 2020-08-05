# Local Development Environment

I am sharing my scripts so that we can be on the same page when we work together. These are personal scripts, so we will not merge them into an official branch, but we can share it.

# Script Descriptions

`so.cmd` - change the current configuration to release (i.e. switch to optimized)
`sd.cmd` - change the current configuration to debug (i.e. switch to debug)

These script only impact the CoreCLR build, the libraries are always build optimized.

`bc.cmd` - build only the CoreCLR, use `so` or `sd` to switch.

`blo.cmd` - build the optimized library, it is always optimized.

`bctg.cmd` - build the `CORE_ROOT`, it is a folder that the test needs and it has the runtime as well as the libraries. `bctg` requires both `bc` and `blo`. This is required before running the tests to make sure the test consume the just built binaries.

`bs.cmd`   - build a single test named reliability framework. Not need for hot-cold splitting. But we could repurpose it to build some other test in case we want to run 1 test.

`cgd.cmd`  - crossgen system.private.corelib with hot-cold-splitting on and start in debug mode, require `bc` to rebuild the crossgen, and require visual studio to attach to it to make progress (otherwise stuck in the initial infinite loop)

`cgr.cmd`  - crossgen system.private.corelib with hot-cold-splitting on and start in run mode, require `bc` to rebuild the crossgen, but it will not wait on attach and run right away.

`r2r.cmd`  - generate a R2RDump file for System.Private.CoreLib

# CoreLab

CoreLab is a simple hello world application for debugging. Here are some scripts for it.

`CoreLab\p.cmd` - build the CoreLab project and replace the runtime with the just built one, require `bc`.

`CoreLab\r.cmd` - run the CoreLab project, requires `p`.

`CoreLab\d.cmd` - debug the CoreLab project using WinDBG, requires `p`, requires `WinDBG`, hard coded path. It will automatically run the content in `autodbg.script`.

`CoreLab\cgd.cmd`  - crossgen CoreLab with hot-cold-splitting on and start in debug mode, require `bc` to rebuild the crossgen, and require visual studio to attach to it to make progress (otherwise stuck in the initial infinite loop)

`CoreLab\cgr.cmd`  - crossgen CoreLab with hot-cold-splitting on and start in run mode, require `bc` to rebuild the crossgen, but it will not wait on attach and run right away.

`CoreLab\r2r.cmd`  - generate a R2RDump file for CoreLab

`CoreLab\log_on.cmd` - Turn on EH logging to debug exception handling related issues

`CoreLab\log_off.cmd` - Turn on EH logging to debug exception handling related issues