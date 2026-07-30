#pragma once
#include "resource.h"
#include <shlobj.h>
#include <atlbase.h>
#include <atlcom.h>

// GUID pour CCopyHook
// {7B896489-3F1E-4E7B-8B2B-987813C12A34}
static const CLSID CLSID_CopyHook =
{ 0x7b896489, 0x3f1e, 0x4e7b, { 0x8b, 0x2b, 0x98, 0x78, 0x13, 0xc1, 0x2a, 0x34 } };

class ATL_NO_VTABLE CCopyHook :
    public CComObjectRootEx<CComSingleThreadModel>,
    public CComCoClass<CCopyHook, &CLSID_CopyHook>,
    public ICopyHookW
{
public:
    CCopyHook() {}

DECLARE_REGISTRY_RESOURCEID(IDR_COPYHOOK)

BEGIN_COM_MAP(CCopyHook)
    COM_INTERFACE_ENTRY(ICopyHookW)
    COM_INTERFACE_ENTRY_IID(IID_IShellCopyHookW, ICopyHookW)
END_COM_MAP()

    DECLARE_PROTECT_FINAL_CONSTRUCT()

    HRESULT FinalConstruct()
    {
        return S_OK;
    }

    void FinalRelease()
    {
    }

    // ICopyHookW
    STDMETHOD_(UINT, CopyCallback)(
        HWND hwnd,
        UINT wFunc,
        UINT wFlags,
        LPCWSTR pszSrcFile,
        DWORD dwSrcAttribs,
        LPCWSTR pszDestFile,
        DWORD dwDestAttribs);
};

OBJECT_ENTRY_AUTO(CLSID_CopyHook, CCopyHook)
