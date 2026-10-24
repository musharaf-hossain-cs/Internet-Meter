#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shobjidl.h>
#include <shlguid.h>
#include <shellapi.h>
#include <strsafe.h>
#include <uxtheme.h>
#include <new>
#include <string>
#include <cstring>

namespace
{
    const CLSID CLSID_InternetMeterDeskBand =
        {0x40b96627, 0x7938, 0x4c3e, {0xa9, 0x32, 0x0b, 0x0e, 0x79, 0xe5, 0x0c, 0x92}};
    const wchar_t WindowClassName[] = L"InternetMeter.NativeDeskBand.Window";
    const wchar_t MapName[] = L"Local\\InternetMeter.TaskbarData.v1";
    const wchar_t MutexName[] = L"Local\\InternetMeter.TaskbarDataLock.v1";
    const wchar_t ShowEventName[] = L"Local\\InternetMeter.ShowDashboard.v1";
    constexpr DWORD ProtocolMagic = 0x494D5452;
    constexpr DWORD ProtocolVersion = 1;
    constexpr int MapCapacity = 512;
    constexpr int DownloadLengthOffset = 16;
    constexpr int DownloadTextOffset = 20;
    constexpr int TextCapacity = 96;
    constexpr int UploadLengthOffset = 116;
    constexpr int UploadTextOffset = 120;
    constexpr int CompactOffset = 216;
    constexpr int DownloadColorOffset = 220;
    constexpr int UploadColorOffset = 224;

    HINSTANCE moduleInstance = nullptr;
    long dllReferences = 0;

    void Log(const char* message)
    {
        char path[MAX_PATH] = {};
        DWORD length = GetEnvironmentVariableA("LOCALAPPDATA", path, ARRAYSIZE(path));
        if (!length || length >= ARRAYSIZE(path)) return;
        StringCchCatA(path, ARRAYSIZE(path), "\\Internet Meter");
        CreateDirectoryA(path, nullptr);
        StringCchCatA(path, ARRAYSIZE(path), "\\deskband-native.log");
        HANDLE file = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
            OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        DWORD written = 0;
        WriteFile(file, message, static_cast<DWORD>(strlen(message)), &written, nullptr);
        WriteFile(file, "\r\n", 2, &written, nullptr);
        CloseHandle(file);
    }

    COLORREF ToColorRef(DWORD argb)
    {
        return RGB((argb >> 16) & 0xff, (argb >> 8) & 0xff, argb & 0xff);
    }

    std::wstring ReadUtf8(const BYTE* data, int lengthOffset, int textOffset)
    {
        int length = *reinterpret_cast<const int*>(data + lengthOffset);
        if (length < 0 || length > TextCapacity) return L"--";
        if (length == 0) return L"";
        int chars = MultiByteToWideChar(CP_UTF8, 0, reinterpret_cast<const char*>(data + textOffset), length,
            nullptr, 0);
        if (chars <= 0) return L"--";
        std::wstring result(chars, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, reinterpret_cast<const char*>(data + textOffset), length,
            &result[0], chars);
        return result;
    }

    void StartMeter(bool showDashboard)
    {
        HANDLE activation = OpenEventW(EVENT_MODIFY_STATE, FALSE, ShowEventName);
        if (activation)
        {
            if (showDashboard) SetEvent(activation);
            CloseHandle(activation);
            return;
        }

        wchar_t executable[MAX_PATH] = {};
        GetModuleFileNameW(moduleInstance, executable, ARRAYSIZE(executable));
        wchar_t* slash = wcsrchr(executable, L'\\');
        if (!slash) return;
        StringCchCopyW(slash + 1, ARRAYSIZE(executable) - static_cast<size_t>(slash + 1 - executable),
            L"InternetMeter.exe");
        ShellExecuteW(nullptr, L"open", executable, showDashboard ? nullptr : L"--background", nullptr, SW_SHOWNORMAL);
    }

    class DeskBand final : public IDeskBand2, public IObjectWithSite, public IInputObject, public IPersistStream
    {
    public:
        DeskBand() : references_(1)
        {
            InterlockedIncrement(&dllReferences);
            Log("DeskBand constructed");
        }

        ~DeskBand()
        {
            SetSite(nullptr);
            Log("DeskBand destroyed");
            InterlockedDecrement(&dllReferences);
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (riid == IID_IUnknown || riid == IID_IOleWindow || riid == IID_IDockingWindow ||
                riid == IID_IDeskBand || riid == IID_IDeskBand2)
                *value = static_cast<IDeskBand2*>(this);
            else if (riid == IID_IObjectWithSite)
                *value = static_cast<IObjectWithSite*>(this);
            else if (riid == IID_IInputObject)
                *value = static_cast<IInputObject*>(this);
            else if (riid == IID_IPersist || riid == IID_IPersistStream)
                *value = static_cast<IPersistStream*>(this);
            else
                return E_NOINTERFACE;
            AddRef();
            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&references_); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG value = InterlockedDecrement(&references_);
            if (!value) delete this;
            return value;
        }

        IFACEMETHODIMP GetWindow(HWND* window) override
        {
            if (!window) return E_POINTER;
            *window = window_;
            return window_ ? S_OK : E_FAIL;
        }
        IFACEMETHODIMP ContextSensitiveHelp(BOOL) override { return E_NOTIMPL; }
        IFACEMETHODIMP ShowDW(BOOL show) override
        {
            Log(show ? "ShowDW true" : "ShowDW false");
            if (window_) ShowWindow(window_, show ? SW_SHOW : SW_HIDE);
            return S_OK;
        }
        IFACEMETHODIMP CloseDW(DWORD) override
        {
            if (window_)
            {
                ShowWindow(window_, SW_HIDE);
                DestroyWindow(window_);
                window_ = nullptr;
            }
            return S_OK;
        }
        IFACEMETHODIMP ResizeBorderDW(const RECT*, IUnknown*, BOOL) override { return E_NOTIMPL; }

        IFACEMETHODIMP GetBandInfo(DWORD bandId, DWORD, DESKBANDINFO* info) override
        {
            if (!info) return E_POINTER;
            bandId_ = bandId;
            char trace[160] = {};
            StringCchPrintfA(trace, ARRAYSIZE(trace), "GetBandInfo id=%lu mask=0x%08lx",
                bandId, info->dwMask);
            Log(trace);
            if (info->dwMask & DBIM_MINSIZE) info->ptMinSize = {104, 22};
            if (info->dwMask & DBIM_MAXSIZE) info->ptMaxSize.y = -1;
            if (info->dwMask & DBIM_INTEGRAL) info->ptIntegral.y = 1;
            if (info->dwMask & DBIM_ACTUAL) info->ptActual = {compact_ ? 210 : 118, 28};
            if (info->dwMask & DBIM_TITLE)
                info->dwMask &= ~DBIM_TITLE;
            if (info->dwMask & DBIM_MODEFLAGS)
                info->dwModeFlags = DBIMF_NORMAL | DBIMF_VARIABLEHEIGHT;
            if (info->dwMask & DBIM_BKCOLOR)
                info->dwMask &= ~DBIM_BKCOLOR;
            return S_OK;
        }

        IFACEMETHODIMP CanRenderComposited(BOOL* canRender) override
        {
            if (!canRender) return E_POINTER;
            *canRender = TRUE;
            return S_OK;
        }
        IFACEMETHODIMP SetCompositionState(BOOL enabled) override
        {
            compositionEnabled_ = enabled;
            if (window_) InvalidateRect(window_, nullptr, TRUE);
            return S_OK;
        }
        IFACEMETHODIMP GetCompositionState(BOOL* enabled) override
        {
            if (!enabled) return E_POINTER;
            *enabled = compositionEnabled_;
            return S_OK;
        }

        IFACEMETHODIMP SetSite(IUnknown* site) override
        {
            if (window_)
            {
                DestroyWindow(window_);
                window_ = nullptr;
            }
            if (inputSite_) { inputSite_->Release(); inputSite_ = nullptr; }
            if (site_) { site_->Release(); site_ = nullptr; }
            if (!site) return S_OK;

            site_ = site;
            site_->AddRef();
            site_->QueryInterface(IID_PPV_ARGS(&inputSite_));
            IOleWindow* oleWindow = nullptr;
            HRESULT result = site_->QueryInterface(IID_PPV_ARGS(&oleWindow));
            if (FAILED(result)) return result;
            HWND parent = nullptr;
            result = oleWindow->GetWindow(&parent);
            oleWindow->Release();
            if (FAILED(result)) return result;
            char parentTrace[96] = {};
            StringCchPrintfA(parentTrace, ARRAYSIZE(parentTrace), "SetSite parent=0x%p", parent);
            Log(parentTrace);

            WNDCLASSW windowClass = {};
            windowClass.lpfnWndProc = WindowProcedure;
            windowClass.hInstance = moduleInstance;
            windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            windowClass.lpszClassName = WindowClassName;
            windowClass.hbrBackground = nullptr;
            RegisterClassW(&windowClass);

            window_ = CreateWindowExW(0, WindowClassName, nullptr,
                WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 0, 0, parent, nullptr, moduleInstance, this);
            if (!window_) return HRESULT_FROM_WIN32(GetLastError());
            char windowTrace[96] = {};
            StringCchPrintfA(windowTrace, ARRAYSIZE(windowTrace), "Created child=0x%p", window_);
            Log(windowTrace);
            SetTimer(window_, 1, 500, nullptr);
            ReadSharedData();
            StartMeter(false);
            return S_OK;
        }

        IFACEMETHODIMP GetSite(REFIID riid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            return site_ ? site_->QueryInterface(riid, value) : E_FAIL;
        }

        IFACEMETHODIMP UIActivateIO(BOOL activate, MSG*) override
        {
            if (activate && window_) SetFocus(window_);
            return S_OK;
        }
        IFACEMETHODIMP HasFocusIO() override
        {
            return window_ && (GetFocus() == window_ || IsChild(window_, GetFocus())) ? S_OK : S_FALSE;
        }
        IFACEMETHODIMP TranslateAcceleratorIO(MSG*) override { return S_FALSE; }

        IFACEMETHODIMP GetClassID(CLSID* classId) override
        {
            if (!classId) return E_POINTER;
            *classId = CLSID_InternetMeterDeskBand;
            return S_OK;
        }
        IFACEMETHODIMP IsDirty() override { return S_FALSE; }
        IFACEMETHODIMP Load(IStream*) override { return S_OK; }
        IFACEMETHODIMP Save(IStream*, BOOL) override { return S_OK; }
        IFACEMETHODIMP GetSizeMax(ULARGE_INTEGER* size) override
        {
            if (!size) return E_POINTER;
            size->QuadPart = 0;
            return S_OK;
        }

    private:
        static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
        {
            DeskBand* self = reinterpret_cast<DeskBand*>(GetWindowLongPtrW(window, GWLP_USERDATA));
            if (message == WM_NCCREATE)
            {
                auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
                self = static_cast<DeskBand*>(create->lpCreateParams);
                SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
            }
            if (!self) return DefWindowProcW(window, message, wParam, lParam);

            switch (message)
            {
            case WM_TIMER:
                self->ReadSharedData();
                return 0;
            case WM_ERASEBKGND:
                return 1;
            case WM_PAINT:
                self->Paint();
                return 0;
            case WM_LBUTTONUP:
                StartMeter(true);
                return 0;
            case WM_SETFOCUS:
                if (self->inputSite_) self->inputSite_->OnFocusChangeIS(static_cast<IDeskBand2*>(self), TRUE);
                return 0;
            case WM_KILLFOCUS:
                if (self->inputSite_) self->inputSite_->OnFocusChangeIS(static_cast<IDeskBand2*>(self), FALSE);
                return 0;
            case WM_DESTROY:
                KillTimer(window, 1);
                return 0;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
            }
        }

        void ReadSharedData()
        {
            HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, MutexName);
            if (!mutex) { StartMeter(false); return; }
            DWORD wait = WaitForSingleObject(mutex, 20);
            if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) { CloseHandle(mutex); return; }
            HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, MapName);
            if (mapping)
            {
                const BYTE* data = static_cast<const BYTE*>(MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, MapCapacity));
                if (data)
                {
                    if (*reinterpret_cast<const DWORD*>(data) == ProtocolMagic &&
                        *reinterpret_cast<const DWORD*>(data + 4) == ProtocolVersion)
                    {
                        download_ = ReadUtf8(data, DownloadLengthOffset, DownloadTextOffset);
                        upload_ = ReadUtf8(data, UploadLengthOffset, UploadTextOffset);
                        compact_ = *reinterpret_cast<const int*>(data + CompactOffset) != 0;
                        downloadColor_ = ToColorRef(*reinterpret_cast<const DWORD*>(data + DownloadColorOffset));
                        uploadColor_ = ToColorRef(*reinterpret_cast<const DWORD*>(data + UploadColorOffset));
                    }
                    UnmapViewOfFile(data);
                }
                CloseHandle(mapping);
            }
            ReleaseMutex(mutex);
            CloseHandle(mutex);
            if (window_) InvalidateRect(window_, nullptr, FALSE);
        }

        void Paint()
        {
            PAINTSTRUCT paint = {};
            HDC dc = BeginPaint(window_, &paint);
            RECT rect = {};
            GetClientRect(window_, &rect);

            // Repaint the portion of the taskbar covered by this child window instead
            // of filling it with a deskband-owned color. This leaves only the meter
            // text visible and follows the composited deskband rendering contract.
            DrawThemeParentBackground(window_, dc, &rect);
            SetBkMode(dc, TRANSPARENT);

            UINT dpi = 96;
            HMODULE user32 = GetModuleHandleW(L"user32.dll");
            using GetDpiForWindowFunction = UINT(WINAPI*)(HWND);
            auto getDpi = reinterpret_cast<GetDpiForWindowFunction>(GetProcAddress(user32, "GetDpiForWindow"));
            if (getDpi) dpi = getDpi(window_);
            HFONT font = CreateFontW(-MulDiv(9, dpi, 72), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
            HFONT previous = static_cast<HFONT>(SelectObject(dc, font));

            if (compact_)
            {
                RECT first = {4, 0, rect.right / 2, rect.bottom};
                RECT second = {rect.right / 2, 0, rect.right - 2, rect.bottom};
                DrawLine(dc, first, L"\x2193 ", download_, downloadColor_);
                DrawLine(dc, second, L"\x2191 ", upload_, uploadColor_);
            }
            else
            {
                int middle = (rect.bottom - rect.top) / 2;
                RECT first = {4, 0, rect.right - 2, middle + 1};
                RECT second = {4, middle - 1, rect.right - 2, rect.bottom};
                DrawLine(dc, first, L"\x2193 ", download_, downloadColor_);
                DrawLine(dc, second, L"\x2191 ", upload_, uploadColor_);
            }

            SelectObject(dc, previous);
            DeleteObject(font);
            EndPaint(window_, &paint);
        }

        static void DrawLine(HDC dc, RECT rect, const wchar_t* arrow, const std::wstring& value, COLORREF arrowColor)
        {
            SetTextColor(dc, arrowColor);
            RECT arrowRect = rect;
            DrawTextW(dc, arrow, -1, &arrowRect, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
            SIZE arrowSize = {};
            GetTextExtentPoint32W(dc, arrow, static_cast<int>(wcslen(arrow)), &arrowSize);
            rect.left += arrowSize.cx;
            SetTextColor(dc, RGB(225, 225, 225));
            DrawTextW(dc, value.c_str(), -1, &rect,
                DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS);
        }

        long references_;
        HWND window_ = nullptr;
        IUnknown* site_ = nullptr;
        IInputObjectSite* inputSite_ = nullptr;
        DWORD bandId_ = 0;
        BOOL compositionEnabled_ = FALSE;
        bool compact_ = false;
        std::wstring download_ = L"0.0 KB/s";
        std::wstring upload_ = L"0.0 KB/s";
        COLORREF downloadColor_ = RGB(56, 189, 248);
        COLORREF uploadColor_ = RGB(167, 139, 250);
    };

    class ClassFactory final : public IClassFactory
    {
    public:
        IFACEMETHODIMP QueryInterface(REFIID riid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (riid != IID_IUnknown && riid != IID_IClassFactory) return E_NOINTERFACE;
            *value = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&references_); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG value = InterlockedDecrement(&references_);
            if (!value) delete this;
            return value;
        }
        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** value) override
        {
            if (outer) return CLASS_E_NOAGGREGATION;
            DeskBand* band = new (std::nothrow) DeskBand();
            if (!band) return E_OUTOFMEMORY;
            HRESULT result = band->QueryInterface(riid, value);
            band->Release();
            return result;
        }
        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&dllReferences); else InterlockedDecrement(&dllReferences);
            return S_OK;
        }
    private:
        long references_ = 1;
    };
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        moduleInstance = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow()
{
    return dllReferences == 0 ? S_OK : S_FALSE;
}

extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** value)
{
    if (clsid != CLSID_InternetMeterDeskBand) return CLASS_E_CLASSNOTAVAILABLE;
    ClassFactory* factory = new (std::nothrow) ClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT result = factory->QueryInterface(riid, value);
    factory->Release();
    return result;
}
