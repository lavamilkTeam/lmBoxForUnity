#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

// Owns a WKWebView attached to an existing Unity NSWindow, never a new window.
@interface LMBoxPanel : NSObject <WKUIDelegate, WKNavigationDelegate>
@property(nonatomic, strong) WKWebView *view;
@property(nonatomic, copy) NSString *origin;
@end

@implementation LMBoxPanel
- (void)webView:(WKWebView *)webView runOpenPanelWithParameters:(WKOpenPanelParameters *)parameters
 initiatedByFrame:(WKFrameInfo *)frame completionHandler:(void (^)(NSArray<NSURL *> *))completion {
    NSOpenPanel *panel = [NSOpenPanel openPanel];
    panel.canChooseFiles = YES;
    panel.canChooseDirectories = parameters.allowsDirectories;
    panel.allowsMultipleSelection = parameters.allowsMultipleSelection;
    [panel beginSheetModalForWindow:webView.window completionHandler:^(NSModalResponse result) {
        completion(result == NSModalResponseOK ? panel.URLs : nil);
    }];
}
- (void)webView:(WKWebView *)webView decidePolicyForNavigationAction:(WKNavigationAction *)action
 decisionHandler:(void (^)(WKNavigationActionPolicy))decision {
    NSURL *url = action.request.URL;
    NSString *origin = [NSString stringWithFormat:@"%@://%@:%@", url.scheme, url.host, url.port];
    BOOL local = [origin isEqualToString:self.origin] || [url.scheme isEqualToString:@"about"] || [url.scheme isEqualToString:@"blob"];
    if (!local && action.navigationType == WKNavigationTypeLinkActivated &&
        ([url.scheme isEqualToString:@"http"] || [url.scheme isEqualToString:@"https"])) {
        [[NSWorkspace sharedWorkspace] openURL:url];
    }
    decision(local ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel);
}
@end

__attribute__((visibility("default"))) void *LMBoxPanel_Create(const char *address) {
    if (![NSThread isMainThread] || !address) return NULL;
    @try {
        NSURL *url = [NSURL URLWithString:[NSString stringWithUTF8String:address]];
        if (![url.scheme isEqualToString:@"http"] || ![url.host isEqualToString:@"127.0.0.1"] || !url.port) return NULL;
        LMBoxPanel *panel = [LMBoxPanel new];
        WKWebViewConfiguration *configuration = [WKWebViewConfiguration new];
        configuration.websiteDataStore = [WKWebsiteDataStore defaultDataStore];
        panel.view = [[WKWebView alloc] initWithFrame:NSMakeRect(0, 0, 100, 100) configuration:configuration];
        panel.view.UIDelegate = panel;
        panel.view.navigationDelegate = panel;
        panel.origin = [NSString stringWithFormat:@"%@://%@:%@", url.scheme, url.host, url.port];
        [panel.view loadRequest:[NSURLRequest requestWithURL:url]];
        return (__bridge_retained void *)panel;
    } @catch (NSException *exception) { return NULL; }
}

__attribute__((visibility("default"))) int LMBoxPanel_SetRect(void *handle, double x, double y, double width, double height) {
    if (![NSThread isMainThread] || !handle || width < 1 || height < 1) return 0;
    @try {
        LMBoxPanel *panel = (__bridge LMBoxPanel *)handle;
        // Unity GUI screen coordinates are points measured from the primary display's top left.
        CGFloat top = NSMaxY([NSScreen screens].firstObject.frame);
        NSRect screenRect = NSMakeRect(x, top - y - height, width, height);
        NSWindow *host = nil;
        for (NSWindow *window in [NSApp orderedWindows]) {
            if (!window.isVisible || window.isMiniaturized || !window.contentView || window.level != NSNormalWindowLevel) continue;
            NSRect content = [window convertRectToScreen:[window.contentView convertRect:window.contentView.bounds toView:nil]];
            if (NSContainsRect(NSInsetRect(content, -1, -1), screenRect)) { host = window; break; }
        }
        if (!host) { panel.view.hidden = YES; return 0; }
        if (panel.view.superview != host.contentView) {
            [panel.view removeFromSuperview];
            [host.contentView addSubview:panel.view positioned:NSWindowAbove relativeTo:nil];
        }
        panel.view.frame = [host.contentView convertRect:[host convertRectFromScreen:screenRect] fromView:nil];
        panel.view.hidden = NO;
        return 1;
    } @catch (NSException *exception) { return 0; }
}

__attribute__((visibility("default"))) void LMBoxPanel_Hide(void *handle) {
    if (![NSThread isMainThread] || !handle) return;
    LMBoxPanel *panel = (__bridge LMBoxPanel *)handle;
    panel.view.hidden = YES;
}

__attribute__((visibility("default"))) void LMBoxPanel_Destroy(void *handle) {
    if (![NSThread isMainThread] || !handle) return;
    LMBoxPanel *panel = (__bridge_transfer LMBoxPanel *)handle;
    [panel.view stopLoading];
    panel.view.UIDelegate = nil;
    panel.view.navigationDelegate = nil;
    [panel.view removeFromSuperview];
    panel.view = nil;
}
