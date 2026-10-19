"""Exercise model discovery through the real published WASM runtime and settings.

Only upstream provider HTTP responses are fixtures. These checks never infer or
use a real provider credential, and discovery cannot enqueue an inference.
"""
from playwright.sync_api import expect


def verify_model_discovery(page, invoke, check, requests, key, output, tag):
    page.get_by_role('tab', name='Settings', exact=True).click()
    provider = page.get_by_label('Model provider', exact=True)
    model = page.get_by_label('Model ID', exact=True)
    credential = page.get_by_label('API key or companion access token', exact=True)
    endpoint = page.get_by_label('Provider endpoint or local companion URL', exact=True)
    context = page.get_by_label('Model context window', exact=True)
    choices = page.get_by_label('Discovered models', exact=True)
    discover = page.get_by_role('button', name='Discover models', exact=True)

    provider.select_option('google')
    credential.fill(key)
    endpoint.fill('')
    context.fill('64000')
    discover.click()
    expect(choices.locator('option[value="fixture-gemini"]')).to_have_count(1)
    expect(choices.locator('option[value="fixture-embedding"]')).to_have_count(0)
    check('discovery lists generating Google models and filters embedding-only models')
    choices.select_option('fixture-gemini')
    expect(model).to_have_value('fixture-gemini')
    expect(context).to_have_value('131072')
    check('selecting a discovered input capacity sets a conservative editable context budget')
    check('Google discovery uses a credential header without exposing it in the URL',
          requests[-1]['headers'].get('x-goog-api-key') == key and key not in requests[-1]['url']
          and requests[-1]['url'].split('?')[0] == 'https://generativelanguage.googleapis.com/v1beta/models')
    context.fill('65536')
    page.get_by_role('button', name='Connect provider', exact=True).click()
    expect(page.get_by_role('tab', name='Conversation', exact=True)).to_have_attribute('aria-selected', 'true')
    check('manual context override reaches the actual agent runtime', invoke('AgentPoll', 0)['contextLimit'] == 65536)

    page.get_by_role('tab', name='Settings', exact=True).click()
    provider.select_option('openai')
    credential.fill(key)
    expect(choices.locator('option[value="fixture-gemini"]')).to_have_count(0)
    discover.click()
    expect(choices.locator('option[value="fixture-manual"]')).to_have_count(1)
    choices.select_option('fixture-manual')
    expect(model).to_have_value('fixture-manual')
    expect(context).to_have_value('65536')
    check('unknown model capacity retains the manual context budget')
    check('OpenAI discovery uses bearer authentication for the list endpoint',
          requests[-1]['headers'].get('authorization') == 'Bearer ' + key
          and requests[-1]['url'] == 'https://api.openai.com/v1/models')

    provider.select_option('anthropic')
    credential.fill(key)
    discover.click()
    expect(choices.locator('option[value="fixture-claude"]')).to_have_count(1)
    choices.select_option('fixture-claude')
    expect(context).to_have_value('65536')
    check('Anthropic discovery leaves unknown capacity editable',
          requests[-1]['headers'].get('x-api-key') == key
          and requests[-1]['headers'].get('anthropic-dangerous-direct-browser-access') == 'true'
          and requests[-1]['url'].split('?')[0] == 'https://api.anthropic.com/v1/models')
    choices.select_option('fixture-claude-limits')
    expect(context).to_have_value('200000')
    check('discovered Anthropic input metadata also sets the editable context budget')
    check('discovery neither exports credentials nor stores them in browser preferences',
          key not in invoke('AgentExport')
          and page.evaluate('(key) => !JSON.stringify({...localStorage,...sessionStorage}).includes(key)', key))
    page.screenshot(path=str(output / f'{tag}-model-discovery.png'), full_page=True)

    # Restore the existing suite's explicit context budget. Selecting a provider
    # is staging configuration; the following configure() performs the connection.
    context.fill('128000')
    provider.select_option('openai')
