{
  "targets": [{
    "target_name": "netdxf_windows",
    "sources": ["atomic_replace.cc"],
    "defines": ["NAPI_VERSION=8"],
    "msvs_settings": {"VCCLCompilerTool": {"ExceptionHandling": 1, "AdditionalOptions": ["/std:c++17", "/W4"]}}
  }]
}
