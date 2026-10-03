// Windows-only filesystem host bridge. Copyright (c) 2026 netDxf contributors.
// MIT License. No DXF processing, CLR, COM, shell, or arbitrary native-call interface.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <node_api.h>
#include <string>
#include <vector>

namespace {
// Do not coerce objects: paths must already be validated JavaScript strings.
bool Path(napi_env env, napi_value value, std::wstring& result) {
  napi_valuetype type;
  size_t size = 0;
  if (napi_typeof(env, value, &type) != napi_ok || type != napi_string ||
      napi_get_value_string_utf16(env, value, nullptr, 0, &size) != napi_ok) {
    napi_throw_type_error(env, "ERR_INVALID_ARG_TYPE", "Replacement paths must be strings.");
    return false;
  }
  if (size == 0 || size > 32766) {
    napi_throw_range_error(env, "ERR_OUT_OF_RANGE", "Replacement path length is outside the Win32 range.");
    return false;
  }
  std::vector<char16_t> chars(size + 1);
  size_t copied = 0;
  if (napi_get_value_string_utf16(env, value, chars.data(), chars.size(), &copied) != napi_ok || copied != size) {
    napi_throw_error(env, "ERR_INVALID_ARG_VALUE", "Unable to read replacement path.");
    return false;
  }
  result.reserve(size);
  for (size_t i = 0; i < size; ++i) {
    if (chars[i] == 0) {
      napi_throw_type_error(env, "ERR_INVALID_ARG_VALUE", "Replacement paths cannot contain NUL.");
      return false;
    }
    result.push_back(static_cast<wchar_t>(chars[i]));
  }
  return true;
}

napi_value Replace(napi_env env, napi_callback_info info) {
  size_t argc = 2;
  napi_value argv[2];
  if (napi_get_cb_info(env, info, &argc, argv, nullptr, nullptr) != napi_ok || argc != 2) {
    napi_throw_type_error(env, "ERR_INVALID_ARG_TYPE", "Replace requires source and destination paths.");
    return nullptr;
  }
  try {
    std::wstring source, destination;
    if (!Path(env, argv[0], source) || !Path(env, argv[1], destination)) return nullptr;
    // Exactly the primitive used by .NET File.Replace(source, destination, null).
    // A held reader remains open on the old file identity. There is no copy/delete fallback.
    DWORD error = ERROR_SUCCESS;
    if (!ReplaceFileW(destination.c_str(), source.c_str(), nullptr, 0, nullptr, nullptr))
      error = GetLastError();
    napi_value result;
    if (napi_create_uint32(env, error, &result) != napi_ok) return nullptr;
    return result;
  } catch (...) {
    napi_throw_error(env, "ERR_NATIVE_ALLOCATION", "Unable to allocate replacement paths.");
    return nullptr;
  }
}

napi_value Init(napi_env env, napi_value exports) {
  napi_value method;
  if (napi_create_function(env, "replaceFile", NAPI_AUTO_LENGTH, Replace, nullptr, &method) != napi_ok ||
      napi_set_named_property(env, exports, "replaceFile", method) != napi_ok) return nullptr;
  return exports;
}
}  // namespace
NAPI_MODULE(NODE_GYP_MODULE_NAME, Init)
