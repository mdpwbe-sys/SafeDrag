#pragma once
#include "resource.h"

class CSafeExplorerHookModule : public ATL::CAtlDllModuleT< CSafeExplorerHookModule >
{
public :
    DECLARE_LIBID(LIBID_SafeExplorerHookLib)
    DECLARE_REGISTRY_APPID_RESOURCEID(IDR_SAFEEXPLORERHOOK, "{1F894451-5D34-4C2E-99A0-39458912B456}")
};

extern class CSafeExplorerHookModule _AtlModule;
