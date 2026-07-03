using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AppleInterop;

namespace Avalonia.Labs.Notifications.Apple;

internal class UNUserNotificationCenter : NSObject
{
    private static readonly unsafe IntPtr s_addCallback = new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&NotificationAddCallback);
    private static readonly unsafe IntPtr s_requestAuthCallback = new((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&RequestAuthorizationCallback);

    private static readonly IntPtr s_class = AppleInterop.UserNotifications.objc_getClass("UNUserNotificationCenter");
    private static readonly IntPtr s_current = Libobjc.sel_getUid("currentNotificationCenter");
    private static readonly IntPtr s_delegate = Libobjc.sel_getUid("setDelegate:");
    private static readonly IntPtr s_addNotificationRequest = Libobjc.sel_getUid("addNotificationRequest:withCompletionHandler:");
    private static readonly IntPtr s_removePendingNotificationRequestsWithIdentifiers = Libobjc.sel_getUid("removePendingNotificationRequestsWithIdentifiers:");
    private static readonly IntPtr s_removeAllPendingNotificationRequests = Libobjc.sel_getUid("removeAllPendingNotificationRequests");
    private static readonly IntPtr s_requestAuthorizationWithOptions = Libobjc.sel_getUid("requestAuthorizationWithOptions:completionHandler:");
    private static readonly IntPtr s_setNotificationCategories = Libobjc.sel_getUid("setNotificationCategories:");

    private NSSet? _currentSet;

    private UNUserNotificationCenter(IntPtr handle) : base(handle, false)
    {
    }

    public void AddWithoutCompletion(UNNotificationRequest request)
    {
        Libobjc.void_objc_msgSend(Handle, s_addNotificationRequest, request.Handle, IntPtr.Zero);
    }

    private static UNUserNotificationCenter? s_currentSingleton;
    public static UNUserNotificationCenter Current
    {
        get
        {
            if (s_currentSingleton is null)
            {
                var handle = Libobjc.intptr_objc_msgSend(s_class, s_current);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("Unable to determine current notification center.");
                s_currentSingleton = new UNUserNotificationCenter(handle);
            }

            return s_currentSingleton;
        }
    }

    public UNUserNotificationCenterDelegate? Delegate
    {
        set
        {
            Libobjc.void_objc_msgSend(Handle, s_delegate, value?.Handle ?? default);
        }
    }

    public void SetNotificationCategories(NSSet categories)
    {
        _currentSet?.Dispose();
        _currentSet = categories;
        Libobjc.void_objc_msgSend(Handle, s_setNotificationCategories, _currentSet.Handle);
    }

    public async Task<bool> RequestAlertAuthorization(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        var id = BlockStateManager.Add(tcs);
        var block = IntPtr.Zero;
        try
        {
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());
            block = BlockLiteral.GetBlockForFunctionPointer(s_requestAuthCallback, new IntPtr(id));
            Libobjc.void_objc_msgSend(Handle, s_requestAuthorizationWithOptions, 1 << 2, block);
            return await tcs.Task;
        }
        finally
        {
            BlockStateManager.Remove(id)?.TrySetCanceled();
            if (block != IntPtr.Zero)
                Libobjc._Block_release(block);
        }
    }

    public async Task Add(UNNotificationRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        var id = BlockStateManager.Add(tcs);
        var block = IntPtr.Zero;
        try
        {
            block = BlockLiteral.GetBlockForFunctionPointer(s_addCallback, new IntPtr(id));
            Libobjc.void_objc_msgSend(Handle, s_addNotificationRequest, request.Handle, block);
            await tcs.Task;
        }
        finally
        {
            BlockStateManager.Remove(id)?.TrySetCanceled();
            if (block != IntPtr.Zero)
                Libobjc._Block_release(block);
        }
    }

    public void RemovePending(string identifier)
    {
        var cfString = CFString.Create(identifier);
        var nsArray = NSArray.WithObjects([cfString.Handle]);
        Libobjc.void_objc_msgSend(Handle, s_removePendingNotificationRequestsWithIdentifiers, nsArray.Handle);
    }

    public void RemoveAllPending()
    {
        Libobjc.void_objc_msgSend(Handle, s_removeAllPendingNotificationRequests);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void NotificationAddCallback(IntPtr thisBlock, IntPtr errorPtr)
    {
        var idPtr = BlockLiteral.TryGetBlockState(thisBlock);
        if (idPtr == IntPtr.Zero)
            return;
        var id = idPtr.ToInt64();
        var tcs = BlockStateManager.Remove(id);
        if (tcs == null)
            return;

        if (errorPtr != IntPtr.Zero)
        {
            using var error = new NSError(errorPtr);
            tcs.TrySetException(new Exception(error.LocalizedDescription ?? "Unknown error"));
        }
        else
        {
            tcs.TrySetResult(true);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void RequestAuthorizationCallback(IntPtr thisBlock, int granted, IntPtr errorPtr)
    {
        var idPtr = BlockLiteral.TryGetBlockState(thisBlock);
        if (idPtr == IntPtr.Zero)
            return;
        var id = idPtr.ToInt64();
        var tcs = BlockStateManager.Remove(id);
        if (tcs == null)
            return;

        if (errorPtr != IntPtr.Zero)
        {
            using var error = new NSError(errorPtr);
            tcs.TrySetException(new Exception(error.LocalizedDescription ?? "Unknown error"));
        }
        else
        {
            tcs.TrySetResult(granted == 1);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _currentSet?.Dispose();
        }
    }
}
