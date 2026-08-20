// TIMF.Bootstrap — x86 native DLL injected into Terraria.exe
// Attaches to the already-running CLR v4 and starts TIMF.Core.Loader.Initialize.
//
// mscorlib.tlb vtable slots (x86, oVft/4):
//   _AppDomain.CreateInstanceFrom(BSTR, BSTR) -> slot 38
//   _ObjectHandle.Unwrap() -> slot 14

#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif

#include <windows.h>
#include <objbase.h>
#include <oleauto.h>
#include <unknwn.h>
#include <string>

// ---- GUIDs ----
static const GUID CLSID_CLRMetaHost =
{ 0x9280188d, 0x0e8e, 0x4867, { 0xb3, 0x0c, 0x7f, 0xa8, 0x38, 0x84, 0xe8, 0xde } };
static const GUID IID_ICLRMetaHost =
{ 0xd332db9e, 0xb9b3, 0x4125, { 0x82, 0x07, 0xa1, 0x48, 0x84, 0xf5, 0x32, 0x16 } };
static const GUID IID_ICLRRuntimeInfo =
{ 0xbd39d1d2, 0xba2f, 0x486a, { 0x89, 0xb0, 0xb4, 0xb0, 0xcb, 0x46, 0x68, 0x91 } };
static const GUID CLSID_CLRRuntimeHost =
{ 0x90f1a06c, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };
static const GUID IID_ICLRRuntimeHost =
{ 0x90f1a06e, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };
static const GUID CLSID_CorRuntimeHost =
{ 0xcb2f6723, 0xab3a, 0x11d2, { 0x9c, 0x40, 0x00, 0xc0, 0x4f, 0xa3, 0x0a, 0x3e } };
static const GUID IID_ICorRuntimeHost =
{ 0xcb2f6722, 0xab3a, 0x11d2, { 0x9c, 0x40, 0x00, 0xc0, 0x4f, 0xa3, 0x0a, 0x3e } };
// mscorlib._AppDomain
static const GUID IID_AppDomain =
{ 0x05f696dc, 0x2b29, 0x3663, { 0xad, 0x8b, 0xc4, 0x38, 0x9c, 0xf2, 0xa7, 0x13 } };
// mscorlib._ObjectHandle
static const GUID IID_ObjectHandle =
{ 0xea675b47, 0x64e0, 0x3b5f, { 0x9b, 0xe7, 0xf7, 0xdc, 0x29, 0x90, 0x73, 0x0d } };

typedef HRESULT (STDAPICALLTYPE *FN_CLRCreateInstance)(REFCLSID, REFIID, LPVOID*);
typedef HRESULT (STDAPICALLTYPE *FN_CorBindToRuntimeEx)(
    LPCWSTR, LPCWSTR, DWORD, REFCLSID, REFIID, LPVOID*);

// ICLRMetaHost after IUnknown:
// 3 GetRuntime, 4 GetVersionFromFile, 5 EnumerateInstalledRuntimes, 6 EnumerateLoadedRuntimes
typedef HRESULT (STDMETHODCALLTYPE *FN_GetRuntime)(IUnknown*, LPCWSTR, REFIID, LPVOID*);
typedef HRESULT (STDMETHODCALLTYPE *FN_EnumerateLoadedRuntimes)(IUnknown*, HANDLE, IUnknown**);
typedef HRESULT (STDMETHODCALLTYPE *FN_EnumNext)(IUnknown*, ULONG, IUnknown**, ULONG*);
typedef HRESULT (STDMETHODCALLTYPE *FN_GetInterface)(IUnknown*, REFCLSID, REFIID, LPVOID*);
typedef HRESULT (STDMETHODCALLTYPE *FN_HostStart)(IUnknown*);
typedef HRESULT (STDMETHODCALLTYPE *FN_ExecuteInDefaultAppDomain)(
    IUnknown*, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, DWORD*);
typedef HRESULT (STDMETHODCALLTYPE *FN_GetDefaultDomain)(IUnknown*, IUnknown**);

// _AppDomain.CreateInstanceFrom — mscorlib.tlb slot 38 (oVft 152)
typedef HRESULT (STDMETHODCALLTYPE *FN_CreateInstanceFrom)(
    IUnknown* This, BSTR assemblyFile, BSTR typeName, IUnknown** pRetVal);

// _ObjectHandle.Unwrap — mscorlib.tlb slot 14 (oVft 56)
typedef HRESULT (STDMETHODCALLTYPE *FN_Unwrap)(IUnknown* This, VARIANT* pRetVal);

// EntryPoint (AutoDual): IUnknown+IDispatch+Object members then Run
// Object: ToString=7, Equals=8, GetHashCode=9, GetType=10; Run ≈ 11
typedef HRESULT (STDMETHODCALLTYPE *FN_EntryRun)(IUnknown* This, BSTR home, long* pRetVal);

struct VTable { void* slots[64]; };

static void* Slot(IUnknown* iface, int index)
{
    return (*reinterpret_cast<VTable**>(iface))->slots[index];
}

static void WriteLog(const std::wstring& line)
{
    wchar_t temp[MAX_PATH];
    GetTempPathW(MAX_PATH, temp);
    std::wstring path = std::wstring(temp) + L"timf-bootstrap.log";
    HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE)
        return;
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t buf[2048];
    wsprintfW(buf, L"[%04d-%02d-%02d %02d:%02d:%02d] %s\r\n",
              st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, line.c_str());
    int nbytes = WideCharToMultiByte(CP_UTF8, 0, buf, -1, NULL, 0, NULL, NULL);
    if (nbytes > 1)
    {
        std::string utf8(static_cast<size_t>(nbytes - 1), '\0');
        WideCharToMultiByte(CP_UTF8, 0, buf, -1, &utf8[0], nbytes, NULL, NULL);
        DWORD written = 0;
        WriteFile(h, utf8.data(), (DWORD)utf8.size(), &written, NULL);
    }
    CloseHandle(h);
}

static void LogHr(const wchar_t* what, HRESULT hr)
{
    wchar_t msg[256];
    wsprintfW(msg, L"%s hr=0x%08X", what, (unsigned)hr);
    WriteLog(msg);
}

static std::wstring GetModuleDirectory(HMODULE mod)
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(mod, path, MAX_PATH);
    std::wstring s(path);
    size_t pos = s.find_last_of(L"\\/");
    if (pos == std::wstring::npos)
        return L".";
    return s.substr(0, pos);
}

static std::wstring ResolveHome(HMODULE self)
{
    wchar_t env[32768];
    DWORD n = GetEnvironmentVariableW(L"TIMF_HOME", env, 32768);
    if (n > 0 && n < 32768)
        return std::wstring(env);

    return GetModuleDirectory(self);
}

static bool WaitForClrModule(DWORD timeoutMs)
{
    DWORD start = GetTickCount();
    while (GetTickCount() - start < timeoutMs)
    {
        if (GetModuleHandleW(L"clr.dll") != NULL || GetModuleHandleW(L"mscorwks.dll") != NULL)
        {
            Sleep(2500);
            WriteLog(L"CLR module present, settled");
            return true;
        }
        Sleep(100);
    }
    return false;
}

static HRESULT ExecuteViaClrHost(IUnknown* host, const std::wstring& coreDll, const std::wstring& home)
{
    FN_HostStart start = reinterpret_cast<FN_HostStart>(Slot(host, 3));
    HRESULT hr = start(host);
    LogHr(L"ICLRRuntimeHost.Start", hr);

    DWORD ret = 0;
    FN_ExecuteInDefaultAppDomain exec =
        reinterpret_cast<FN_ExecuteInDefaultAppDomain>(Slot(host, 11));
    hr = exec(host, coreDll.c_str(), L"TIMF.Core.Loader", L"Initialize", home.c_str(), &ret);
    wchar_t msg[256];
    wsprintfW(msg, L"ExecuteInDefaultAppDomain hr=0x%08X ret=%u", (unsigned)hr, (unsigned)ret);
    WriteLog(msg);
    return hr;
}

static HRESULT GetHostsFromMetaHost(IUnknown** outClrHost, IUnknown** outCorHost)
{
    *outClrHost = NULL;
    *outCorHost = NULL;

    HMODULE mscoree = GetModuleHandleW(L"mscoree.dll");
    if (!mscoree)
        mscoree = LoadLibraryW(L"mscoree.dll");
    if (!mscoree)
        return E_FAIL;

    FN_CLRCreateInstance create =
        reinterpret_cast<FN_CLRCreateInstance>(GetProcAddress(mscoree, "CLRCreateInstance"));
    if (!create)
        return E_FAIL;

    IUnknown* meta = NULL;
    HRESULT hr = create(CLSID_CLRMetaHost, IID_ICLRMetaHost, (void**)&meta);
    LogHr(L"CLRCreateInstance", hr);
    if (FAILED(hr) || !meta)
        return hr;

    // EnumerateLoadedRuntimes = slot 6
    IUnknown* enumUnk = NULL;
    FN_EnumerateLoadedRuntimes enumLoaded =
        reinterpret_cast<FN_EnumerateLoadedRuntimes>(Slot(meta, 6));
    hr = enumLoaded(meta, GetCurrentProcess(), &enumUnk);
    LogHr(L"EnumerateLoadedRuntimes(slot6)", hr);

    if (SUCCEEDED(hr) && enumUnk)
    {
        for (;;)
        {
            IUnknown* info = NULL;
            ULONG fetched = 0;
            FN_EnumNext next = reinterpret_cast<FN_EnumNext>(Slot(enumUnk, 3));
            HRESULT nhr = next(enumUnk, 1, &info, &fetched);
            if (nhr != S_OK || fetched == 0 || !info)
                break;

            WriteLog(L"Loaded runtime info obtained");
            FN_GetInterface getIface = reinterpret_cast<FN_GetInterface>(Slot(info, 9));

            if (!*outClrHost)
            {
                IUnknown* host = NULL;
                HRESULT ghr = getIface(info, CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (void**)&host);
                LogHr(L"  GetInterface(ICLRRuntimeHost)", ghr);
                if (SUCCEEDED(ghr) && host)
                    *outClrHost = host;
            }
            if (!*outCorHost)
            {
                IUnknown* host = NULL;
                HRESULT ghr = getIface(info, CLSID_CorRuntimeHost, IID_ICorRuntimeHost, (void**)&host);
                LogHr(L"  GetInterface(ICorRuntimeHost)", ghr);
                if (SUCCEEDED(ghr) && host)
                    *outCorHost = host;
            }
            info->Release();
        }
        enumUnk->Release();
    }

    if (!*outClrHost || !*outCorHost)
    {
        IUnknown* info = NULL;
        FN_GetRuntime getRuntime = reinterpret_cast<FN_GetRuntime>(Slot(meta, 3));
        hr = getRuntime(meta, L"v4.0.30319", IID_ICLRRuntimeInfo, (void**)&info);
        LogHr(L"GetRuntime(v4.0.30319)", hr);
        if (SUCCEEDED(hr) && info)
        {
            FN_GetInterface getIface = reinterpret_cast<FN_GetInterface>(Slot(info, 9));
            if (!*outClrHost)
            {
                IUnknown* host = NULL;
                HRESULT ghr = getIface(info, CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (void**)&host);
                LogHr(L"GetRuntime GetInterface(ICLRRuntimeHost)", ghr);
                if (SUCCEEDED(ghr) && host)
                    *outClrHost = host;
            }
            if (!*outCorHost)
            {
                IUnknown* host = NULL;
                HRESULT ghr = getIface(info, CLSID_CorRuntimeHost, IID_ICorRuntimeHost, (void**)&host);
                LogHr(L"GetRuntime GetInterface(ICorRuntimeHost)", ghr);
                if (SUCCEEDED(ghr) && host)
                    *outCorHost = host;
            }
            info->Release();
        }
    }

    meta->Release();
    return (*outClrHost || *outCorHost) ? S_OK : E_FAIL;
}

static HRESULT GetCorHost_CorBind(IUnknown** outHost)
{
    *outHost = NULL;
    HMODULE mscoree = GetModuleHandleW(L"mscoree.dll");
    if (!mscoree)
        mscoree = LoadLibraryW(L"mscoree.dll");
    if (!mscoree)
        return E_FAIL;

    FN_CorBindToRuntimeEx bind =
        reinterpret_cast<FN_CorBindToRuntimeEx>(GetProcAddress(mscoree, "CorBindToRuntimeEx"));
    if (!bind)
        return E_FAIL;

    IUnknown* host = NULL;
    HRESULT hr = bind(NULL, L"wks", 0, CLSID_CorRuntimeHost, IID_ICorRuntimeHost, (void**)&host);
    LogHr(L"CorBind(NULL, ICorRuntimeHost)", hr);
    if (SUCCEEDED(hr) && host)
    {
        *outHost = host;
        return S_OK;
    }

    host = NULL;
    hr = bind(L"v4.0.30319", L"wks", 0, CLSID_CorRuntimeHost, IID_ICorRuntimeHost, (void**)&host);
    LogHr(L"CorBind(v4, ICorRuntimeHost)", hr);
    if (SUCCEEDED(hr) && host)
    {
        *outHost = host;
        return S_OK;
    }
    return hr;
}

static HRESULT ExecuteViaCorHost(IUnknown* corHost, const std::wstring& coreDll, const std::wstring& home)
{
    FN_HostStart start = reinterpret_cast<FN_HostStart>(Slot(corHost, 10));
    HRESULT hr = start(corHost);
    LogHr(L"ICorRuntimeHost.Start", hr);

    IUnknown* domainUnk = NULL;
    FN_GetDefaultDomain getDef = reinterpret_cast<FN_GetDefaultDomain>(Slot(corHost, 13));
    hr = getDef(corHost, &domainUnk);
    LogHr(L"GetDefaultDomain", hr);
    if (FAILED(hr) || !domainUnk)
        return hr;

    IUnknown* appDomain = NULL;
    hr = domainUnk->QueryInterface(IID_AppDomain, (void**)&appDomain);
    LogHr(L"QI _AppDomain", hr);
    if (FAILED(hr) || !appDomain)
    {
        // Some hosts already return _AppDomain from GetDefaultDomain
        appDomain = domainUnk;
        appDomain->AddRef();
        WriteLog(L"Using GetDefaultDomain pointer as AppDomain");
    }

    BSTR bFile = SysAllocString(coreDll.c_str());
    BSTR bType = SysAllocString(L"TIMF.Core.EntryPoint");

    IUnknown* handleUnk = NULL;
    FN_CreateInstanceFrom createFrom =
        reinterpret_cast<FN_CreateInstanceFrom>(Slot(appDomain, 38));
    hr = createFrom(appDomain, bFile, bType, &handleUnk);
    LogHr(L"CreateInstanceFrom(slot 38)", hr);

    // Fallback nearby slots if type library layout differs slightly
    if (FAILED(hr) || !handleUnk)
    {
        static const int alts[] = { 37, 39, 40, 42 };
        for (int i = 0; i < 4 && (FAILED(hr) || !handleUnk); i++)
        {
            handleUnk = NULL;
            createFrom = reinterpret_cast<FN_CreateInstanceFrom>(Slot(appDomain, alts[i]));
            hr = createFrom(appDomain, bFile, bType, &handleUnk);
            wchar_t msg[96];
            wsprintfW(msg, L"CreateInstanceFrom(slot %d) hr=0x%08X", alts[i], (unsigned)hr);
            WriteLog(msg);
        }
    }

    SysFreeString(bFile);
    SysFreeString(bType);

    if (FAILED(hr) || !handleUnk)
    {
        appDomain->Release();
        domainUnk->Release();
        return FAILED(hr) ? hr : E_FAIL;
    }

    // Prefer QI _ObjectHandle then Unwrap at slot 14
    IUnknown* objHandle = NULL;
    HRESULT qhr = handleUnk->QueryInterface(IID_ObjectHandle, (void**)&objHandle);
    LogHr(L"QI _ObjectHandle", qhr);
    if (FAILED(qhr) || !objHandle)
    {
        objHandle = handleUnk;
        objHandle->AddRef();
    }

    VARIANT objVar;
    VariantInit(&objVar);
    FN_Unwrap unwrap = reinterpret_cast<FN_Unwrap>(Slot(objHandle, 14));
    hr = unwrap(objHandle, &objVar);
    LogHr(L"Unwrap(slot 14)", hr);
    if (FAILED(hr))
    {
        for (int s = 7; s <= 16 && FAILED(hr); s++)
        {
            VariantClear(&objVar);
            VariantInit(&objVar);
            unwrap = reinterpret_cast<FN_Unwrap>(Slot(objHandle, s));
            hr = unwrap(objHandle, &objVar);
            wchar_t msg[80];
            wsprintfW(msg, L"Unwrap(slot %d) hr=0x%08X vt=%d", s, (unsigned)hr, (int)objVar.vt);
            WriteLog(msg);
            if (SUCCEEDED(hr) && (objVar.vt == VT_DISPATCH || objVar.vt == VT_UNKNOWN))
                break;
            hr = E_FAIL;
        }
    }

    objHandle->Release();
    handleUnk->Release();

    if (FAILED(hr) || (objVar.vt != VT_DISPATCH && objVar.vt != VT_UNKNOWN))
    {
        WriteLog(L"Unwrap failed or empty");
        VariantClear(&objVar);
        appDomain->Release();
        domainUnk->Release();
        return E_FAIL;
    }

    IUnknown* entry = (objVar.vt == VT_DISPATCH)
        ? static_cast<IUnknown*>(objVar.pdispVal)
        : objVar.punkVal;

    BSTR bHome = SysAllocString(home.c_str());
    long ret = -1;
    hr = E_FAIL;
    // AutoDual EntryPoint.Run after Object members => typically slot 11
    static const int runSlots[] = { 11, 12, 13, 10, 14, 7, 8, 9 };
    for (int i = 0; i < 8; i++)
    {
        int s = runSlots[i];
        FN_EntryRun run = reinterpret_cast<FN_EntryRun>(Slot(entry, s));
        long local = -1;
        HRESULT rhr = run(entry, bHome, &local);
        wchar_t msg[96];
        wsprintfW(msg, L"EntryPoint.Run(slot %d) hr=0x%08X ret=%ld", s, (unsigned)rhr, local);
        WriteLog(msg);
        if (SUCCEEDED(rhr))
        {
            ret = local;
            hr = S_OK;
            break;
        }
    }
    SysFreeString(bHome);

    VariantClear(&objVar);
    appDomain->Release();
    domainUnk->Release();

    if (SUCCEEDED(hr))
    {
        wchar_t msg[64];
        wsprintfW(msg, L"Managed entry finished ret=%ld", ret);
        WriteLog(msg);
    }
    return hr;
}

static HRESULT HostClrAndStart(const std::wstring& home)
{
    WriteLog(L"Resolve TIMF home");

    if (!WaitForClrModule(120000))
    {
        WriteLog(L"Timed out waiting for clr.dll");
        return E_FAIL;
    }

    std::wstring coreDll = home + L"\\TIMF.Core.dll";
    if (GetFileAttributesW(coreDll.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        WriteLog(L"TIMF.Core.dll missing next to bootstrap");
        return E_FAIL;
    }

    IUnknown* clrHost = NULL;
    IUnknown* corHost = NULL;
    GetHostsFromMetaHost(&clrHost, &corHost);

    if (clrHost)
    {
        WriteLog(L"Path: ICLRRuntimeHost");
        HRESULT hr = ExecuteViaClrHost(clrHost, coreDll, home);
        clrHost->Release();
        if (SUCCEEDED(hr))
            return S_OK;
        WriteLog(L"ICLRRuntimeHost failed, trying CorHost...");
    }

    if (!corHost)
        GetCorHost_CorBind(&corHost);

    if (corHost)
    {
        WriteLog(L"Path: ICorRuntimeHost + _AppDomain vtable");
        HRESULT hr = ExecuteViaCorHost(corHost, coreDll, home);
        corHost->Release();
        return hr;
    }

    WriteLog(L"No usable CLR host");
    return E_FAIL;
}

static DWORD WINAPI BootstrapThread(LPVOID param)
{
    HMODULE self = (HMODULE)param;
    Sleep(500);
    HostClrAndStart(ResolveHome(self));
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        WriteLog(L"DllMain PROCESS_ATTACH");
        HANDLE t = CreateThread(NULL, 0, BootstrapThread, hModule, 0, NULL);
        if (t)
            CloseHandle(t);
    }
    return TRUE;
}
