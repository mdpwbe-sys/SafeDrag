#include "stdafx.h"
#include "resource.h"
#include "dllmain.h"

CSafeExplorerHookModule _AtlModule;

// DLL Entry Point
extern "C" BOOL WINAPI DllMain(HINSTANCE hInstance, DWORD dwReason, LPVOID lpReserved)
{
    hInstance;
    return _AtlModule.DllMain(dwReason, lpReserved);
}

// Used to determine whether the DLL can be unloaded by OLE
STDAPI DllCanUnloadNow(void)
{
    return _AtlModule.DllCanUnloadNow();
}

// Returns a class factory to create an object of the requested type
STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)
{
    return _AtlModule.DllGetClassObject(rclsid, riid, ppv);
}

static void RegisterCopyHookHandler(const wchar_t* keyPath, const wchar_t* clsidStr)
{
    HKEY hKey;
    if (RegCreateKeyExW(HKEY_CLASSES_ROOT, keyPath, 0, nullptr, REG_OPTION_NON_VOLATILE, KEY_WRITE, nullptr, &hKey, nullptr) == ERROR_SUCCESS)
    {
        RegSetValueExW(hKey, nullptr, 0, REG_SZ, (const BYTE*)clsidStr, (DWORD)((wcslen(clsidStr) + 1) * sizeof(wchar_t)));
        RegCloseKey(hKey);
    }
}

static void UnregisterCopyHookHandler(const wchar_t* keyPath)
{
    RegDeleteKeyW(HKEY_CLASSES_ROOT, keyPath);
}

// DllRegisterServer - Adds entries to the system registry
STDAPI DllRegisterServer(void)
{
    HRESULT hr = _AtlModule.DllRegisterServer(FALSE);
    if (FAILED(hr))
        return hr;

    const wchar_t* clsidStr = L"{7B896489-3F1E-4E7B-8B2B-987813C12A34}";

    RegisterCopyHookHandler(L"Directory\\ShellEx\\CopyHookHandlers\\SafeExplorer", clsidStr);
    RegisterCopyHookHandler(L"Folder\\ShellEx\\CopyHookHandlers\\SafeExplorer", clsidStr);
    RegisterCopyHookHandler(L"Drive\\ShellEx\\CopyHookHandlers\\SafeExplorer", clsidStr);

    return S_OK;
}

// DllUnregisterServer - Removes entries from the system registry
STDAPI DllUnregisterServer(void)
{
    UnregisterCopyHookHandler(L"Directory\\ShellEx\\CopyHookHandlers\\SafeExplorer");
    UnregisterCopyHookHandler(L"Folder\\ShellEx\\CopyHookHandlers\\SafeExplorer");
    UnregisterCopyHookHandler(L"Drive\\ShellEx\\CopyHookHandlers\\SafeExplorer");

    return _AtlModule.DllUnregisterServer(FALSE);
}
