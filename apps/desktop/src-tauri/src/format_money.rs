/// Agrupa um decimal exato para leitura, no mesmo padrão de `apps/web/src/lib/trading/format.ts`.
/// Não converte para ponto flutuante.
pub fn group_decimal(value: &str) -> Result<String, ()> {
    if !plain_decimal(value) {
        return Err(());
    }
    let negative = value.starts_with('-');
    let unsigned = if negative { &value[1..] } else { value };
    let (whole, fraction) = match unsigned.split_once('.') {
        Some((whole, fraction)) => (whole, Some(fraction)),
        None => (unsigned, None),
    };
    let mut grouped = String::new();
    let chars: Vec<char> = whole.chars().collect();
    for (index, character) in chars.iter().enumerate() {
        if index > 0 && (chars.len() - index).is_multiple_of(3) {
            grouped.push('.');
        }
        grouped.push(*character);
    }
    let decimals = fraction
        .map(|digits| format!(",{digits}"))
        .unwrap_or_default();
    let sign = if negative { "-" } else { "" };
    Ok(format!("{sign}{grouped}{decimals}"))
}

fn plain_decimal(value: &str) -> bool {
    let digits = value.strip_prefix('-').unwrap_or(value);
    let mut parts = digits.split('.');
    let Some(whole) = parts.next() else {
        return false;
    };
    let fraction = parts.next();
    if parts.next().is_some()
        || whole.is_empty()
        || !whole.bytes().all(|byte| byte.is_ascii_digit())
    {
        return false;
    }
    match fraction {
        None => true,
        Some(fraction) => {
            !fraction.is_empty() && fraction.bytes().all(|byte| byte.is_ascii_digit())
        }
    }
}

#[cfg(test)]
mod tests {
    use super::group_decimal;

    #[test]
    fn groups_like_the_web() {
        assert_eq!(group_decimal("1000").unwrap(), "1.000");
        assert_eq!(group_decimal("872.01").unwrap(), "872,01");
        assert_eq!(
            group_decimal("1.000000000000000000000000001").unwrap(),
            "1,000000000000000000000000001"
        );
        assert_eq!(group_decimal("-1234567.89").unwrap(), "-1.234.567,89");
    }
}
