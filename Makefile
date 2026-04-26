VERSION ?= v0.0.1
RELEASE_PREFIX ?= release

.PHONY: release
release:
	@set -eu; \
	if ! git diff --quiet || ! git diff --cached --quiet; then \
		echo "Working tree is not clean. Commit or stash changes before creating a release tag." >&2; \
		exit 1; \
	fi; \
	stamp=$$(date '+%y%m%d-%H%M'); \
	tag="$(RELEASE_PREFIX)/$(VERSION)-$$stamp"; \
	if git rev-parse "$$tag" >/dev/null 2>&1; then \
		echo "Tag already exists: $$tag" >&2; \
		exit 1; \
	fi; \
	git tag -a "$$tag" -m "Release $$tag"; \
	git push origin "$$tag"; \
	echo "Created and pushed $$tag"
