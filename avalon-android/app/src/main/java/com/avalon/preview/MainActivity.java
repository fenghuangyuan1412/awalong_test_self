package com.avalon.preview;

import android.app.Activity;
import android.os.Bundle;
import android.view.WindowManager;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

/**
 * 阿瓦隆预览版 · 安卓壳
 * 承载本地 H5 游戏（assets/index.html + core.js）。
 * 针对热座（传递设备）玩法的三点适配：
 *  1. 屏幕常亮 —— 传设备过程中不熄屏；
 *  2. 返回键防误触 —— 第一次提示、2 秒内再按才收起，避免对局中误退出；
 *  3. 竖屏锁定 + configChanges —— 旋转不重建 Activity，游戏状态不丢失。
 */
public class MainActivity extends Activity {

    private WebView web;
    private long lastBack = 0;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        web = new WebView(this);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        web.setWebViewClient(new WebViewClient());
        web.setBackgroundColor(0xFF10152A);
        setContentView(web);
        web.loadUrl("file:///android_asset/index.html");
    }

    @Override
    public void onBackPressed() {
        long now = System.currentTimeMillis();
        if (now - lastBack < 2000) {
            moveTaskToBack(true);
        } else {
            lastBack = now;
            Toast.makeText(this, "对局进行中：再按一次收起游戏（不会退出对局）", Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onDestroy() {
        if (web != null) web.destroy();
        super.onDestroy();
    }
}
