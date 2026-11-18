"""把关卡数据嵌进排键试验台网页。

关卡数据（砖块时间）属于关卡作者，不进仓库：先用 tools/Preview.cs 的 --export 导出成 json，
再用这个脚本把它们注入 tools/layout-lab.html，输出到仓库外的路径用于发布。

用法：python tools/build-layout-lab.py 输出.html key=导出1.json key=导出2.json ...
（key 只能用字母、数字、- 和 _，会作为网页里保存结论用的关卡编号）
"""

import json
import re
import sys
from pathlib import Path

PLACEHOLDER = "/*LEVELS*/[]"


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    template = Path(__file__).with_name("layout-lab.html").read_text(encoding="utf-8")
    shared_reference = '<script src="review-editor/note-planner.js"></script>'
    if shared_reference not in template:
        raise ValueError("Shared planner reference missing from layout-lab template")
    shared = Path(__file__).with_name("review-editor").joinpath("note-planner.js").read_text(encoding="utf-8")
    template = template.replace(shared_reference, "<script>\n" + re.sub(r"</script", r"<\\/script", shared, flags=re.I) + "\n</script>")
    if PLACEHOLDER not in template:
        print("模板里找不到数据占位符 /*LEVELS*/[]", file=sys.stderr)
        return 1

    levels = []
    for arg in sys.argv[2:]:
        key, _, path = arg.partition("=")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", key) or not path:
            print(f"参数格式应为 key=文件.json：{arg}", file=sys.stderr)
            return 2
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        data["key"] = key
        levels.append(data)

    out = Path(sys.argv[1])
    # A chart title must never close the inline script in the portable output.
    payload = json.dumps(levels, ensure_ascii=False, separators=(",", ":")).replace("<", "\\u003c")
    out.write_text(template.replace(PLACEHOLDER, payload), encoding="utf-8")
    print(f"已生成 {out}（{len(levels)} 个关卡，{out.stat().st_size // 1024} KB）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
