/*
 * 推荐引擎：根据问卷答案 + 本机硬件，从目录中挑选软件。
 * 纯函数，无 DOM 依赖；浏览器中挂到 window.Recommend，Node 中 module.exports。
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.Recommend = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  var LIMITS = { easy: 3, best: 6, private: 4 };

  var NEED_UNCOVERED_REASON = {
    offline: {
      search: '完全离线时无法联网搜索',
      voice: '本地语音对话工具目前不够成熟，离线环境暂不推荐',
      create: '离线环境下暂无适合普通用户的图片/视频生成工具',
      office: '离线环境下可用本地模型辅助写作，但没有专门的办公 AI 工具'
    }
  };

  /** 判断某个软件在当前网络/硬件条件下能否使用。 */
  function availability(app, network, hw) {
    if (app.region === 'global' && network !== 'global') {
      return { ok: false, reason: '需要能访问海外服务的网络' };
    }
    if (app.region === 'cn' && network === 'offline') {
      return { ok: false, reason: '需要联网使用' };
    }
    var req = app.requires;
    if (req && hw) {
      if (req.avx2 && hw.avx2 === false) {
        return { ok: false, reason: '这台电脑的 CPU 不支持 AVX2 指令集' };
      }
      if (req.ramGB && typeof hw.ramGB === 'number' && hw.ramGB > 0 && hw.ramGB < req.ramGB - 1.5) {
        return { ok: false, reason: '内存不足 ' + req.ramGB + 'GB，本地模型会非常卡' };
      }
    }
    return { ok: true };
  }

  /** 某个软件对某个用途的加权得分；基础分低于 2 视为不适合该用途。 */
  function needScore(app, need, prefs) {
    var base = (app.scores && app.scores[need]) || 0;
    if (base < 2) return 0;
    var s = base;
    if (prefs.priority === 'private') s += app.region === 'local' ? 1.5 : -1;
    if (prefs.priority === 'easy' && app.advanced) s -= 1;
    // 能访问海外服务时，同分情况下优先海外头部产品
    if (prefs.network === 'global' && app.region === 'global') s += 0.3;
    if (app.kind === 'web') s -= 0.4;
    if (app.install && app.install.source === 'manual') s -= 0.3;
    return Math.max(s, 0.1);
  }

  function covers(app, need) {
    return ((app.scores && app.scores[need]) || 0) >= 2;
  }

  function isDiscreteGpu(name) {
    return /nvidia|geforce|rtx|gtx|quadro|radeon rx|radeon pro|arc a\d|arc b\d/i.test(name || '');
  }

  /** 根据内存/显存给出本地模型建议（Qwen3 系列，Q4 量化）。 */
  function modelAdvice(hw) {
    var tiers = [
      { key: '1.7b', label: 'Qwen3 1.7B', ollama: 'qwen3:1.7b', lmKey: 'qwen/qwen3-1.7b', sizeGB: 1.4, lmSearch: 'qwen3 1.7b', level: '入门' },
      { key: '4b', label: 'Qwen3 4B', ollama: 'qwen3:4b', lmKey: 'qwen/qwen3-4b', sizeGB: 2.5, lmSearch: 'qwen3 4b', level: '基础' },
      { key: '8b', label: 'Qwen3 8B', ollama: 'qwen3:8b', lmKey: 'qwen/qwen3-8b', sizeGB: 5.2, lmSearch: 'qwen3 8b', level: '标准' },
      { key: '14b', label: 'Qwen3 14B', ollama: 'qwen3:14b', lmKey: 'qwen/qwen3-14b', sizeGB: 9.3, lmSearch: 'qwen3 14b', level: '进阶' }
    ];
    var ram = hw && typeof hw.ramGB === 'number' ? hw.ramGB : 0;
    var vram = 0;
    var gpuName = '';
    ((hw && hw.gpus) || []).forEach(function (g) {
      if (isDiscreteGpu(g.name) && (g.vramGB || 0) > vram) { vram = g.vramGB || 0; gpuName = g.name; }
    });
    var idx;
    if (!ram) idx = 1;
    else if (ram >= 28) idx = 3;
    else if (ram >= 14) idx = 2;
    else if (ram >= 7) idx = 1;
    else idx = 0;
    if (vram >= 16 && ram >= 14) idx = 3;
    else if (vram >= 8 && ram >= 12 && idx < 2) idx = 2;

    var tier = tiers[idx];
    var reason;
    if (!ram) reason = '未能读取内存容量，先按 8GB 内存给出保守建议';
    else if (vram >= 8 && isDiscreteGpu(gpuName)) reason = '检测到 ' + Math.round(ram) + 'GB 内存和 ' + gpuName + '（' + vram + 'GB 显存），可以流畅运行该规格';
    else reason = '检测到 ' + Math.round(ram) + 'GB 内存，该规格在纯 CPU 下也能运行';
    var warn = [];
    if (hw && hw.avx2 === false) warn.push('CPU 不支持 AVX2，LM Studio 无法运行，建议使用 Ollama 或 GPT4All，速度会偏慢');
    if (ram && ram < 7) warn.push('内存低于 8GB，本地模型只能做简单问答，建议优先使用在线 AI');
    return {
      tier: tier.key,
      label: tier.label,
      level: tier.level,
      ollama: tier.ollama,
      lmSearch: tier.lmSearch,
      lmKey: tier.lmKey,
      sizeGB: tier.sizeGB,
      reason: reason,
      warnings: warn,
      tiers: tiers
    };
  }

  function appWarnings(app, hw) {
    var out = [];
    if (app.requires && hw) {
      if (app.requires.avx2 && hw.avx2 == null) out.push('未能确认 CPU 是否支持 AVX2');
      if (app.requires.ramGB && hw.ramGB && hw.ramGB < 14) out.push('内存小于 16GB，只适合运行较小的模型');
    }
    return out;
  }

  /**
   * @param catalog  catalog.json 内容
   * @param answers  { needs: [], network: 'cn'|'global'|'offline', priority: 'easy'|'best'|'private' }
   * @param hw       硬件信息 { ramGB, avx2, gpus }
   * @param installed 可选：{ appId: true } 已安装的软件
   */
  function recommend(catalog, answers, hw, installed) {
    installed = installed || {};
    var needs = (answers.needs && answers.needs.length) ? answers.needs.slice() : ['chat'];
    var prefs = {
      network: answers.network || 'cn',
      priority: answers.priority || 'easy'
    };
    var limit = LIMITS[prefs.priority] || 3;

    var usable = [];
    var blocked = {};
    catalog.apps.forEach(function (app) {
      var a = availability(app, prefs.network, hw);
      if (a.ok) usable.push(app);
      else blocked[app.id] = a.reason;
    });

    var picked = [];
    var pickedIds = {};
    function pick(app) { if (!pickedIds[app.id]) { pickedIds[app.id] = true; picked.push(app); } }

    if (prefs.priority === 'best') {
      // 每个用途取前两名
      needs.forEach(function (need) {
        usable
          .filter(function (app) { return needScore(app, need, prefs) > 0; })
          .sort(function (a, b) { return needScore(b, need, prefs) - needScore(a, need, prefs) || totalScore(b) - totalScore(a); })
          .slice(0, 2)
          .forEach(pick);
      });
      picked = picked.slice(0, limit);
      pickedIds = {};
      picked.forEach(function (a) { pickedIds[a.id] = true; });
    } else {
      // 贪心覆盖：每轮挑能覆盖最多剩余用途的软件
      var remaining = needs.slice();
      while (remaining.length && picked.length < limit) {
        var best = null, bestGain = 0;
        usable.forEach(function (app) {
          if (pickedIds[app.id]) return;
          var gain = 0;
          remaining.forEach(function (need) { gain += needScore(app, need, prefs); });
          if (gain <= 0) return;
          // 平分时偏向综合能力更强、且已安装的
          gain += totalScore(app) * 0.01 + (installed[app.id] ? 0.05 : 0);
          if (gain > bestGain) { bestGain = gain; best = app; }
        });
        if (!best) break;
        pick(best);
        remaining = remaining.filter(function (need) { return !covers(best, need); });
      }
    }

    function totalScore(app) {
      var t = 0;
      needs.forEach(function (n) { t += (app.scores && app.scores[n]) || 0; });
      return t;
    }

    var uncovered = needs.filter(function (need) {
      return !picked.some(function (app) { return covers(app, need); });
    }).map(function (need) {
      var reason = (NEED_UNCOVERED_REASON[prefs.network] || {})[need] || '当前条件下没有合适的工具';
      return { need: need, reason: reason };
    });

    function describe(app, primary) {
      var reasons = needs.filter(function (n) { return covers(app, n); }).map(function (n) {
        return { need: n, text: (app.why && app.why[n]) || '' };
      });
      return {
        id: app.id,
        primary: primary,
        installed: !!installed[app.id],
        reasons: reasons,
        warnings: appWarnings(app, hw)
      };
    }

    var picks = picked.map(function (app) { return describe(app, true); });

    // 备选：每个用途中未入选的最佳软件
    var altIds = {};
    var alternates = [];
    needs.forEach(function (need) {
      usable
        .filter(function (app) { return !pickedIds[app.id] && !altIds[app.id] && needScore(app, need, prefs) > 0; })
        .sort(function (a, b) { return needScore(b, need, prefs) - needScore(a, need, prefs); })
        .slice(0, 2)
        .forEach(function (app) { altIds[app.id] = true; alternates.push(describe(app, false)); });
    });
    alternates = alternates.slice(0, 6);

    var wantsLocal = picked.some(function (a) { return a.region === 'local'; });
    return {
      answers: { needs: needs, network: prefs.network, priority: prefs.priority },
      picks: picks,
      alternates: alternates,
      uncovered: uncovered,
      blocked: blocked,
      model: wantsLocal ? modelAdvice(hw) : null
    };
  }

  /** 由网络探测结果推断问卷第 2 题的默认选项。 */
  function guessNetwork(net) {
    if (!net) return null;
    if (net.global) return 'global';
    if (net.cn) return 'cn';
    if (net.cn === false && net.global === false) return 'offline';
    return null;
  }

  return {
    recommend: recommend,
    availability: availability,
    modelAdvice: modelAdvice,
    guessNetwork: guessNetwork,
    isDiscreteGpu: isDiscreteGpu
  };
});
