#include "c_api.h"

#include <windows.h>

#include "engine.h"

using namespace MetaTraderEngine;

namespace
{
    static HWND ToHwnd(uint64_t value)
    {
        return reinterpret_cast<HWND>(static_cast<uintptr_t>(value));
    }

    static uint64_t FromHwnd(HWND hwnd)
    {
        return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(hwnd));
    }
}

MT_API int mt_is_valid_window(uint64_t hwnd)
{
    auto h = ToHwnd(hwnd);
    return (h != NULL && IsWindow(h)) ? 1 : 0;
}

MT_API uint64_t mt_find_list_view(uint64_t parentHwnd)
{
    auto hList = FindListView(ToHwnd(parentHwnd));
    return FromHwnd(hList);
}

MT_API void* mt_create_context(uint64_t listViewHwnd)
{
    auto ctx = CreateContext(ToHwnd(listViewHwnd));
    return reinterpret_cast<void*>(ctx);
}

MT_API void* mt_create_context_from_parent(uint64_t parentHwnd)
{
    auto hList = FindListView(ToHwnd(parentHwnd));
    if (!hList)
    {
        return nullptr;
    }

    auto ctx = CreateContext(hList);
    return reinterpret_cast<void*>(ctx);
}

MT_API int mt_update_row_count(void* ctx)
{
    auto* context = reinterpret_cast<Context*>(ctx);
    return UpdateRowCount(context);
}

MT_API int mt_close_position_mt5(void* ctx, int rowIdx)
{
    auto* context = reinterpret_cast<Context*>(ctx);
    return ClosePositionMT5(context, rowIdx) ? 1 : 0;
}

MT_API void mt_destroy_context(void* ctx)
{
    auto* context = reinterpret_cast<Context*>(ctx);
    delete context;
}

MT_API int mt_click_buy(uint64_t chartHwnd)
{
    auto hChart = ToHwnd(chartHwnd);
    if (!hChart || !IsWindow(hChart))
    {
        return 0;
    }

    // One-click trading area is typically near top-left on MT chart windows.
    // Keep behavior simple/best-effort and non-invasive.
    PostClick(hChart, 40, 15);
    return 1;
}

MT_API int mt_click_sell(uint64_t chartHwnd)
{
    auto hChart = ToHwnd(chartHwnd);
    if (!hChart || !IsWindow(hChart))
    {
        return 0;
    }

    // One-click trading area is typically near top-left on MT chart windows.
    // Keep behavior simple/best-effort and non-invasive.
    PostClick(hChart, 120, 15);
    return 1;
}
