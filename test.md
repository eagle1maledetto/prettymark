# PrettyMark Test File

This is a **test file** to verify that PrettyMark renders Markdown correctly.

## Table Test

| Feature | Status | Notes |
|---------|--------|-------|
| Tables | OK | Renders correctly |
| Code blocks | OK | With syntax highlighting |
| Task lists | OK | Checkboxes work |
| Live reload | OK | 300ms debounce |

## Code Block Test

```python
def hello(name: str) -> str:
    """Greet someone."""
    return f"Hello, {name}!"

if __name__ == "__main__":
    print(hello("PrettyMark"))
```

```javascript
const greet = (name) => {
  console.log(`Hello, ${name}!`);
};
```

## Task List Test

- [x] Markdown rendering
- [x] GitHub-flavored CSS
- [x] Syntax highlighting
- [ ] Live reload
- [ ] PyInstaller build

## Blockquote

> PrettyMark is a simple, native Markdown viewer for Windows.
> It uses pywebview for rendering.

## Links and Images

Visit [GitHub](https://github.com) for more info.

---

*End of test file.*
